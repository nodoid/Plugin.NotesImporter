using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Imports notes from a single source service's export into the unified <see cref="Note"/> model.
/// Implementations are stateless and read from a file or folder on disk. Both a synchronous
/// (streaming) and an asynchronous (materializing) entry point are provided.
/// </summary>
public interface INoteImporter
{
    /// <summary>The service this importer reads from.</summary>
    NoteSource Source { get; }

    /// <summary>
    /// Returns true if <paramref name="path"/> looks like an export this importer can read.
    /// Used by <see cref="NotesImportService"/> for auto-detection.
    /// </summary>
    bool CanImport(string path);

    /// <summary>
    /// Reads the export at <paramref name="path"/> (a file or a folder, depending on the
    /// source) and yields one <see cref="Note"/> per note found. Lazily streamed.
    /// </summary>
    IEnumerable<Note> Import(string path);

    /// <summary>
    /// Asynchronously reads the export at <paramref name="path"/>, using async file/database I/O,
    /// and returns all notes found.
    /// </summary>
    Task<IReadOnlyList<Note>> ImportAsync(string path, CancellationToken cancellationToken = default);
}
