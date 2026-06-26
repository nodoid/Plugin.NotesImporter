using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// A small, dependency-free HTML reader shared by the HTML-based importers (Apple Notes and
/// OneNote both export each note/page as an <c>.html</c> file). It extracts the title, a plain-text
/// rendering of the body, and any images — whether inlined as <c>data:</c> URIs or linked to files
/// sitting next to the HTML.
/// </summary>
internal static class HtmlNoteParser
{
    /// <summary>Synchronously parse an HTML note file into a <see cref="Note"/>.</summary>
    public static Note Parse(string htmlPath, NoteSource source)
    {
        string html = File.ReadAllText(htmlPath);
        var (note, pending) = ParseCore(html, htmlPath, source);
        foreach (var p in pending)
            note.Images.Add(new NoteImage
            {
                FileName = Path.GetFileName(p.FullPath),
                MediaType = p.MediaType,
                Data = File.ReadAllBytes(p.FullPath),
                SourceReference = p.SourceReference,
            });
        return note;
    }

    /// <summary>Asynchronously parse an HTML note file, using async file I/O for the body and images.</summary>
    public static async Task<Note> ParseAsync(string htmlPath, NoteSource source, CancellationToken ct)
    {
        string html = await File.ReadAllTextAsync(htmlPath, ct).ConfigureAwait(false);
        var (note, pending) = ParseCore(html, htmlPath, source);
        foreach (var p in pending)
            note.Images.Add(new NoteImage
            {
                FileName = Path.GetFileName(p.FullPath),
                MediaType = p.MediaType,
                Data = await File.ReadAllBytesAsync(p.FullPath, ct).ConfigureAwait(false),
                SourceReference = p.SourceReference,
            });
        return note;
    }

    private readonly record struct PendingImage(string FullPath, string MediaType, string SourceReference);

    /// <summary>
    /// Pure parse: extracts everything that needs no binary file I/O (title, text, dates, inline
    /// data-URI images) and returns the set of file-based images still to be loaded.
    /// </summary>
    private static (Note note, List<PendingImage> pending) ParseCore(string html, string htmlPath, NoteSource source)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(htmlPath)) ?? ".";

        var note = new Note
        {
            Id = Path.GetFileNameWithoutExtension(htmlPath),
            Source = source,
            Html = html,
            Title = ExtractTitle(html) ?? Path.GetFileNameWithoutExtension(htmlPath),
            PlainText = HtmlToPlainText(html),
        };

        note.CreatedAt = ExtractMetaDate(html, "created")
                         ?? new DateTimeOffset(File.GetCreationTimeUtc(htmlPath), TimeSpan.Zero);
        note.ModifiedAt = ExtractMetaDate(html, "modified")
                          ?? new DateTimeOffset(File.GetLastWriteTimeUtc(htmlPath), TimeSpan.Zero);

        var pending = new List<PendingImage>();
        foreach (Match m in ImgTag.Matches(html))
        {
            string src = WebUtility.HtmlDecode(m.Groups["src"].Value).Trim();
            if (src.Length == 0) continue;

            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var img = NoteImage.FromDataUri(src);
                if (img != null) note.Images.Add(img); // no IO needed
                continue;
            }

            // Skip remote URLs — we only embed local, exported assets.
            if (src.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                src.StartsWith("//"))
                continue;

            string relative = src.Replace('/', Path.DirectorySeparatorChar);
            string full = Path.GetFullPath(Path.Combine(directory, relative));
            if (File.Exists(full))
                pending.Add(new PendingImage(full, MimeTypes.FromExtension(Path.GetExtension(full)), src));
        }

        return (note, pending);
    }

    private static readonly Regex TitleTag =
        new(@"<title[^>]*>(?<t>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex Heading =
        new(@"<h[1-6][^>]*>(?<t>.*?)</h[1-6]>", RegexOptions.IgnoreCase | RegexOptions.Singleline);

    private static string? ExtractTitle(string html)
    {
        var m = TitleTag.Match(html);
        if (m.Success)
        {
            string t = CleanInline(m.Groups["t"].Value);
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        m = Heading.Match(html);
        if (m.Success)
        {
            string t = CleanInline(m.Groups["t"].Value);
            if (!string.IsNullOrWhiteSpace(t)) return t;
        }

        return null;
    }

    private static readonly Regex ScriptStyle =
        new(@"<(script|style)[^>]*>.*?</\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex BlockBreak =
        new(@"</(p|div|br|li|tr|h[1-6])\s*>|<br\s*/?>", RegexOptions.IgnoreCase);
    private static readonly Regex AnyTag = new(@"<[^>]+>", RegexOptions.Singleline);

    /// <summary>Strips tags to readable plain text, preserving line breaks at block boundaries.</summary>
    public static string HtmlToPlainText(string html)
    {
        string s = ScriptStyle.Replace(html, " ");
        s = BlockBreak.Replace(s, "\n");
        s = AnyTag.Replace(s, string.Empty);
        s = WebUtility.HtmlDecode(s);

        // Collapse runs of blank lines and trailing whitespace per line.
        var lines = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        int blankRun = 0;
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                if (++blankRun > 1) continue;
            }
            else
            {
                blankRun = 0;
            }
            sb.AppendLine(line);
        }

        return sb.ToString().Trim();
    }

    private static string CleanInline(string fragment) =>
        WebUtility.HtmlDecode(AnyTag.Replace(fragment, string.Empty)).Trim();

    private static readonly Regex ImgTag =
        new(@"<img[^>]*\bsrc\s*=\s*(?:""(?<src>[^""]*)""|'(?<src>[^']*)')", RegexOptions.IgnoreCase);

    private static readonly Regex MetaDate =
        new(@"<meta[^>]*\bname\s*=\s*[""'](?<name>[^""']*)[""'][^>]*\bcontent\s*=\s*[""'](?<content>[^""']*)[""']",
            RegexOptions.IgnoreCase);

    private static DateTimeOffset? ExtractMetaDate(string html, string keyword)
    {
        foreach (Match m in MetaDate.Matches(html))
        {
            if (m.Groups["name"].Value.Contains(keyword, StringComparison.OrdinalIgnoreCase) &&
                DateTimeOffset.TryParse(m.Groups["content"].Value, out var dto))
            {
                return dto;
            }
        }

        return null;
    }
}
