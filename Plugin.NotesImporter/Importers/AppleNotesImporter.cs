using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Imports notes from Apple Notes exported as HTML.
/// <para>
/// Apple Notes has no built-in bulk export, so notes are typically exported to a folder of
/// <c>.html</c> files (one per note) using the macOS share sheet or a common exporter utility.
/// Each note's images are either inlined as <c>data:</c> URIs or saved as files referenced from
/// the HTML. Point <see cref="Import"/> at the folder containing the exported notes.
/// </para>
/// <para>For reading Apple's native on-device database directly, see
/// <see cref="AppleNotesSqliteImporter"/>.</para>
/// </summary>
public sealed class AppleNotesImporter : INoteImporter
{
    public NoteSource Source => NoteSource.AppleNotes;

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
            throw new DirectoryNotFoundException($"Apple Notes export path not found: {path}");

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
            throw new DirectoryNotFoundException($"Apple Notes export path not found: {path}");

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
        return ext is ".html" or ".htm";
    }
}
