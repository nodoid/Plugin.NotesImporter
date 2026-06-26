using System.Text;
using System.Text.Json;
using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Imports notes from a <b>Google Takeout</b> export of Google Keep.
/// <para>
/// Take out your data at https://takeout.google.com (select "Keep"). The export contains one
/// <c>.json</c> file per note plus the referenced image/attachment files in the same folder.
/// Point <see cref="Import"/> at that folder (typically <c>Takeout/Keep</c>).
/// </para>
/// <para>Each note JSON has fields such as <c>title</c>, <c>textContent</c>, <c>listContent</c>,
/// <c>attachments</c>, <c>labels</c>, <c>createdTimestampUsec</c> and
/// <c>userEditedTimestampUsec</c> (microseconds since the Unix epoch).</para>
/// </summary>
public sealed class GoogleKeepImporter : INoteImporter
{
    public NoteSource Source => NoteSource.GoogleKeep;

    public bool CanImport(string path)
    {
        if (Directory.Exists(path))
            return EnumerateNoteFiles(path).Any(IsKeepNoteFile);
        return File.Exists(path) && IsKeepNoteFile(path);
    }

    public IEnumerable<Note> Import(string path)
    {
        foreach (var file in ResolveFiles(path))
        {
            Note? note = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var (parsed, pending) = ParseCore(doc.RootElement, file);
                foreach (var p in pending)
                {
                    if (File.Exists(p.FullPath))
                        parsed.Images.Add(LoadImage(p, File.ReadAllBytes(p.FullPath)));
                    else
                        parsed.Metadata[$"missingAttachment:{p.SourceReference}"] = p.MediaType;
                }
                note = parsed;
            }
            catch (JsonException) { }

            if (note != null) yield return note;
        }
    }

    public async Task<IReadOnlyList<Note>> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        var notes = new List<Note>();
        foreach (var file in ResolveFiles(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string json = await File.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(json);
                var (parsed, pending) = ParseCore(doc.RootElement, file);
                foreach (var p in pending)
                {
                    if (File.Exists(p.FullPath))
                        parsed.Images.Add(LoadImage(p,
                            await File.ReadAllBytesAsync(p.FullPath, cancellationToken).ConfigureAwait(false)));
                    else
                        parsed.Metadata[$"missingAttachment:{p.SourceReference}"] = p.MediaType;
                }
                notes.Add(parsed);
            }
            catch (JsonException) { }
        }

        return notes;
    }

    private static IEnumerable<string> ResolveFiles(string path)
    {
        if (File.Exists(path))
        {
            if (IsKeepNoteFile(path)) yield return path;
            yield break;
        }

        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"Google Keep export path not found: {path}");

        foreach (var file in EnumerateNoteFiles(path))
            if (IsKeepNoteFile(file))
                yield return file;
    }

    private readonly record struct PendingAttachment(string FullPath, string MediaType, string SourceReference);

    private static NoteImage LoadImage(PendingAttachment p, byte[] data) => new()
    {
        FileName = Path.GetFileName(p.FullPath),
        MediaType = p.MediaType,
        Data = data,
        SourceReference = p.SourceReference,
    };

    private static IEnumerable<string> EnumerateNoteFiles(string folder) =>
        Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories);

    /// <summary>Cheap check that a .json file is a Keep note (and not, say, Labels.json metadata).</summary>
    private static bool IsKeepNoteFile(string file)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            var root = doc.RootElement;
            return root.ValueKind == JsonValueKind.Object &&
                   (root.TryGetProperty("textContent", out _) ||
                    root.TryGetProperty("listContent", out _) ||
                    root.TryGetProperty("title", out _) && root.TryGetProperty("isArchived", out _));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Parses the note JSON into a <see cref="Note"/>, returning attachments still to be loaded.</summary>
    private static (Note note, List<PendingAttachment> pending) ParseCore(JsonElement root, string file)
    {
        var note = new Note
        {
            Id = Path.GetFileNameWithoutExtension(file),
            Source = NoteSource.GoogleKeep,
            Title = GetString(root, "title"),
            IsArchived = GetBool(root, "isArchived"),
            IsPinned = GetBool(root, "isPinned"),
            IsTrashed = GetBool(root, "isTrashed"),
        };

        // Body: either free text (textContent) or a checklist (listContent).
        var body = new StringBuilder();
        if (root.TryGetProperty("textContent", out var text) && text.ValueKind == JsonValueKind.String)
            body.Append(text.GetString());

        if (root.TryGetProperty("listContent", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in list.EnumerateArray())
            {
                bool isChecked = GetBool(item, "isChecked");
                string itemText = GetString(item, "text") ?? string.Empty;
                if (body.Length > 0) body.Append('\n');
                body.Append(isChecked ? "[x] " : "[ ] ").Append(itemText);
            }
        }

        note.PlainText = body.ToString();

        // Labels.
        if (root.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                string? name = GetString(label, "name");
                if (!string.IsNullOrEmpty(name)) note.Labels.Add(name!);
            }
        }

        // Timestamps are microseconds since the Unix epoch.
        note.CreatedAt = FromUsec(GetLong(root, "createdTimestampUsec"));
        note.ModifiedAt = FromUsec(GetLong(root, "userEditedTimestampUsec"));

        string? color = GetString(root, "color");
        if (!string.IsNullOrEmpty(color) && color != "DEFAULT") note.Metadata["color"] = color!;

        // Attachments live as files next to the JSON.
        var pending = new List<PendingAttachment>();
        string dir = Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".";
        if (root.TryGetProperty("attachments", out var attachments) && attachments.ValueKind == JsonValueKind.Array)
        {
            foreach (var att in attachments.EnumerateArray())
            {
                string? filePath = GetString(att, "filePath");
                if (string.IsNullOrEmpty(filePath)) continue;

                string full = ResolveAttachment(dir, filePath!);
                string mime = GetString(att, "mimetype") ?? MimeTypes.FromExtension(Path.GetExtension(full));
                pending.Add(new PendingAttachment(full, mime, filePath!));
            }
        }

        return (note, pending);
    }

    /// <summary>Keep occasionally rewrites attachment extensions (e.g. .jpeg vs .jpg); fall back by name.</summary>
    private static string ResolveAttachment(string dir, string filePath)
    {
        string direct = Path.GetFullPath(Path.Combine(dir, filePath));
        if (File.Exists(direct)) return direct;

        string stem = Path.GetFileNameWithoutExtension(filePath);
        var match = Directory.EnumerateFiles(dir, stem + ".*").FirstOrDefault();
        return match ?? direct;
    }

    private static DateTimeOffset? FromUsec(long? usec) =>
        usec is > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(usec.Value / 1000) : null;

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static long? GetLong(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : null;
}
