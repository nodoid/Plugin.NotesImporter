using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Imports notes from Microsoft OneNote (or Sticky Notes) exported as HTML.
/// <para>
/// OneNote pages can be saved as <c>.html</c> via <i>File → Export</i>, and the Microsoft Graph
/// API returns page bodies as HTML as well. Either way you end up with one <c>.html</c> file per
/// page, with images inlined as <c>data:</c> URIs or saved alongside the HTML. Point
/// <see cref="Import"/> at the folder of exported pages.
/// </para>
/// </summary>
public sealed class MicrosoftOneNoteImporter : INoteImporter
{
    public NoteSource Source => NoteSource.MicrosoftOneNote;

    public bool CanImport(string path)
    {
        if (Directory.Exists(path))
            return EnumerateHtml(path).Any();
        return File.Exists(path) && IsHtml(path);
    }

    public IEnumerable<Note> Import(string path)
    {
        if (File.Exists(path))
        {
            if (IsHtml(path)) yield return HtmlNoteParser.Parse(path, Source);
            yield break;
        }

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"OneNote export path not found: {path}");

        foreach (var file in EnumerateHtml(path))
            yield return HtmlNoteParser.Parse(file, Source);
    }

    public async Task<IReadOnlyList<Note>> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var notes = new List<Note>();

        if (File.Exists(path))
        {
            if (IsHtml(path))
                notes.Add(await HtmlNoteParser.ParseAsync(path, Source, cancellationToken).ConfigureAwait(false));
            return notes;
        }

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"OneNote export path not found: {path}");

        foreach (var file in EnumerateHtml(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            notes.Add(await HtmlNoteParser.ParseAsync(file, Source, cancellationToken).ConfigureAwait(false));
        }

        return notes;
    }

    private static IEnumerable<string> EnumerateHtml(string folder) =>
        Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories).Where(IsHtml);

    private static bool IsHtml(string file)
    {
        string ext = Path.GetExtension(file).ToLowerInvariant();
        return ext is ".html" or ".htm" or ".mht" or ".mhtml";
    }
}
