using Plugin.NotesImporter.Importers;
using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter;

/// <summary>
/// The library's main entry point. Imports notes from Apple Notes, Google Keep and Microsoft
/// OneNote exports into a single unified, serializable <see cref="Note"/> collection. Every
/// operation is offered in both synchronous and asynchronous (<c>...Async</c>) form.
/// </summary>
/// <example>
/// <code>
/// var service = new NotesImportService();
///
/// // Synchronous, explicit source:
/// var keep  = service.ImportGoogleKeep("/path/to/Takeout/Keep");
///
/// // Asynchronous:
/// var apple = await service.ImportAppleNotesAsync("/path/to/AppleExport");
///
/// // Native Apple Notes database (macOS default location):
/// var native = await service.ImportAppleNotesDatabaseAsync();
///
/// // Auto-detect the export type:
/// var notes = await service.ImportAutoAsync("/some/export/folder");
///
/// // Serialize everything (images embedded as Base64):
/// string json = Note.ToJson(notes);
/// </code>
/// </example>
public sealed class NotesImportService
{
    private readonly Dictionary<NoteSource, INoteImporter> _importers;
    private readonly AppleNotesSqliteImporter _appleDatabase = new();

    public NotesImportService()
    {
        _importers = new INoteImporter[]
        {
            new GoogleKeepImporter(),
            new AppleNotesImporter(),
            new MicrosoftOneNoteImporter(),
        }.ToDictionary(i => i.Source);
    }

    /// <summary>The folder/file importers available, keyed by source. Add or replace to extend the service.</summary>
    public IReadOnlyDictionary<NoteSource, INoteImporter> Importers => _importers;

    /// <summary>The native Apple Notes database (<c>NoteStore.sqlite</c>) reader.</summary>
    public AppleNotesSqliteImporter AppleNotesDatabaseImporter => _appleDatabase;

    // --- generic entry points --------------------------------------------------------------

    /// <summary>Imports from a known source. <paramref name="path"/> is the export file or folder.</summary>
    public IReadOnlyList<Note> Import(NoteSource source, string path) =>
        Resolve(source).Import(path).ToList();

    /// <summary>Asynchronously imports from a known source.</summary>
    public Task<IReadOnlyList<Note>> ImportAsync(NoteSource source, string path,
        CancellationToken cancellationToken = default) =>
        Resolve(source).ImportAsync(path, cancellationToken);

    /// <summary>
    /// Inspects <paramref name="path"/>, picks the first importer that recognizes it, and imports.
    /// Throws if nothing matches.
    /// </summary>
    public IReadOnlyList<Note> ImportAuto(string path) =>
        DetectImporter(path).Import(path).ToList();

    /// <summary>Asynchronous <see cref="ImportAuto"/>.</summary>
    public Task<IReadOnlyList<Note>> ImportAutoAsync(string path, CancellationToken cancellationToken = default) =>
        DetectImporter(path).ImportAsync(path, cancellationToken);

    // --- per-source convenience ------------------------------------------------------------

    public IReadOnlyList<Note> ImportGoogleKeep(string path) => Import(NoteSource.GoogleKeep, path);
    public Task<IReadOnlyList<Note>> ImportGoogleKeepAsync(string path, CancellationToken ct = default) =>
        ImportAsync(NoteSource.GoogleKeep, path, ct);

    public IReadOnlyList<Note> ImportAppleNotes(string path) => Import(NoteSource.AppleNotes, path);
    public Task<IReadOnlyList<Note>> ImportAppleNotesAsync(string path, CancellationToken ct = default) =>
        ImportAsync(NoteSource.AppleNotes, path, ct);

    public IReadOnlyList<Note> ImportOneNote(string path) => Import(NoteSource.MicrosoftOneNote, path);
    public Task<IReadOnlyList<Note>> ImportOneNoteAsync(string path, CancellationToken ct = default) =>
        ImportAsync(NoteSource.MicrosoftOneNote, path, ct);

    /// <summary>
    /// Imports from Apple's native <c>NoteStore.sqlite</c>. Pass null to use the macOS default
    /// location, or an explicit path to the database file (or its containing folder).
    /// </summary>
    public IReadOnlyList<Note> ImportAppleNotesDatabase(string? path = null) =>
        path is null ? _appleDatabase.ImportDefault() : _appleDatabase.Import(path).ToList();

    /// <summary>Asynchronous <see cref="ImportAppleNotesDatabase"/>.</summary>
    public Task<IReadOnlyList<Note>> ImportAppleNotesDatabaseAsync(string? path = null,
        CancellationToken ct = default) =>
        path is null ? _appleDatabase.ImportDefaultAsync(ct) : _appleDatabase.ImportAsync(path, ct);

    // --- internals -------------------------------------------------------------------------

    private INoteImporter Resolve(NoteSource source) =>
        _importers.TryGetValue(source, out var importer)
            ? importer
            : throw new ArgumentOutOfRangeException(nameof(source), source, "No importer registered for this source.");

    private INoteImporter DetectImporter(string path)
    {
        // Order matters: the native DB and Keep's JSON shapes are the most specific, so they are
        // checked before the more permissive HTML importers.
        var candidates = new INoteImporter[]
        {
            _appleDatabase,
            _importers[NoteSource.GoogleKeep],
            _importers[NoteSource.AppleNotes],
            _importers[NoteSource.MicrosoftOneNote],
        };

        foreach (var importer in candidates)
            if (importer.CanImport(path))
                return importer;

        throw new InvalidOperationException(
            $"Could not detect a supported notes export at '{path}'. " +
            "Use Import(source, path) to specify the source explicitly.");
    }
}
