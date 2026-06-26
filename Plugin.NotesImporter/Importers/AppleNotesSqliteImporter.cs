using Microsoft.Data.Sqlite;
using Plugin.NotesImporter.Models;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Imports notes directly from Apple's native <c>NoteStore.sqlite</c> database (the on-device store
/// used by Notes on macOS / iOS). This is the "native" Apple Notes reader: it reads the Core Data
/// backed SQLite database, decompresses each note's gzipped-protobuf body (see
/// <see cref="AppleNotesProto"/>) to recover the text, and resolves note metadata.
/// <para>
/// On macOS the database lives at
/// <c>~/Library/Group Containers/group.com.apple.notes/NoteStore.sqlite</c>. Pass <c>null</c> to
/// <see cref="ImportDefault"/> to use that path, or pass an explicit path (file or its containing
/// folder) to <see cref="Import"/>. Image/attachment extraction is best-effort: attachment binaries
/// are read from the <c>Accounts/&lt;acct&gt;/Media/&lt;media&gt;/</c> folders next to the database
/// when present, and the schema is probed at runtime to tolerate version differences.
/// </para>
/// </summary>
public sealed class AppleNotesSqliteImporter : INoteImporter
{
    public NoteSource Source => NoteSource.AppleNotes;

    /// <summary>Core Data stores dates as seconds since 2001-01-01 UTC.</summary>
    private static readonly DateTimeOffset AppleEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public bool CanImport(string path)
    {
        try
        {
            return ResolveDatabasePath(path) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Imports from the platform default location (macOS group container). Throws elsewhere.</summary>
    public IReadOnlyList<Note> ImportDefault() => Import(DefaultDatabasePath()).ToList();

    /// <inheritdoc cref="ImportDefault"/>
    public Task<IReadOnlyList<Note>> ImportDefaultAsync(CancellationToken cancellationToken = default) =>
        ImportAsync(DefaultDatabasePath(), cancellationToken);

    public IEnumerable<Note> Import(string path)
    {
        string dbPath = RequireDatabase(path);
        string container = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;

        using var conn = OpenReadOnly(dbPath);
        conn.Open();

        var schema = NoteSchema.Discover(conn);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = schema.NotesQuery;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var note = ReadNote(reader, schema);
            LoadAttachments(conn, note, schema, container);
            yield return note;
        }
    }

    public async Task<IReadOnlyList<Note>> ImportAsync(string path, CancellationToken cancellationToken = default)
    {
        string dbPath = RequireDatabase(path);
        string container = Path.GetDirectoryName(Path.GetFullPath(dbPath))!;

        var notes = new List<Note>();
        await using var conn = OpenReadOnly(dbPath);
        await conn.OpenAsync(cancellationToken).ConfigureAwait(false);

        var schema = NoteSchema.Discover(conn);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = schema.NotesQuery;
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var note = ReadNote(reader, schema);
            await LoadAttachmentsAsync(conn, note, schema, container, cancellationToken).ConfigureAwait(false);
            notes.Add(note);
        }

        return notes;
    }

    private static Note ReadNote(SqliteDataReader reader, NoteSchema schema)
    {
        var note = new Note { Source = NoteSource.AppleNotes };

        long? zpk = GetInt64(reader, "pk");
        if (zpk is not null) note.Metadata["z_pk"] = zpk.Value.ToString();
        note.Id = GetString(reader, "ident") ?? zpk?.ToString() ?? Guid.NewGuid().ToString("N");
        note.Title = GetString(reader, "title");
        note.CreatedAt = FromAppleTime(GetDouble(reader, "created"));
        note.ModifiedAt = FromAppleTime(GetDouble(reader, "modified"));
        note.IsPinned = GetInt64(reader, "pinned") == 1;
        note.IsTrashed = GetInt64(reader, "deleted") == 1;

        byte[]? blob = GetBlob(reader, "data");
        string? body = blob is { Length: > 0 } ? AppleNotesProto.ExtractNoteText(blob) : null;

        // The protobuf body normally repeats the title as its first line; keep the full body as text.
        note.PlainText = body ?? GetString(reader, "snippet") ?? string.Empty;
        if (string.IsNullOrEmpty(note.Title))
            note.Title = FirstLine(note.PlainText);

        return note;
    }

    private void LoadAttachments(SqliteConnection conn, Note note, NoteSchema schema, string container)
    {
        foreach (var (path, mime, reference) in EnumerateAttachmentPaths(conn, note, schema, container))
            if (File.Exists(path))
                note.Images.Add(new NoteImage
                {
                    FileName = Path.GetFileName(path),
                    MediaType = mime,
                    Data = File.ReadAllBytes(path),
                    SourceReference = reference,
                });
    }

    private async Task LoadAttachmentsAsync(SqliteConnection conn, Note note, NoteSchema schema,
        string container, CancellationToken ct)
    {
        foreach (var (path, mime, reference) in EnumerateAttachmentPaths(conn, note, schema, container))
            if (File.Exists(path))
                note.Images.Add(new NoteImage
                {
                    FileName = Path.GetFileName(path),
                    MediaType = mime,
                    Data = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false),
                    SourceReference = reference,
                });
    }

    /// <summary>
    /// Best-effort resolution of a note's media attachments to on-disk paths under the container's
    /// <c>Accounts/&lt;acct&gt;/Media/&lt;media&gt;/&lt;file&gt;</c> layout. Returns nothing if the
    /// schema lacks the columns this requires (older databases vary).
    /// </summary>
    private static IEnumerable<(string path, string mime, string reference)> EnumerateAttachmentPaths(
        SqliteConnection conn, Note note, NoteSchema schema, string container)
    {
        if (!schema.SupportsAttachments)
            yield break;
        if (!note.Metadata.TryGetValue("z_pk", out var pkText) || !long.TryParse(pkText, out long zpk))
            yield break;

        var rows = new List<(string mediaUuid, string fileName, string acctUuid)>();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT media.ZIDENTIFIER AS mediaUuid, media.ZFILENAME AS fileName, " +
                "       acct.ZIDENTIFIER AS acctUuid " +
                "FROM ZICCLOUDSYNCINGOBJECT a " +
                "JOIN ZICCLOUDSYNCINGOBJECT media ON a.ZMEDIA = media.Z_PK " +
                "LEFT JOIN ZICCLOUDSYNCINGOBJECT acct ON media.ZACCOUNT = acct.Z_PK " +
                "WHERE a.ZNOTE = $pk AND a.ZMEDIA IS NOT NULL";
            cmd.Parameters.AddWithValue("$pk", zpk);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string? mediaUuid = reader["mediaUuid"] as string;
                string? fileName = reader["fileName"] as string;
                string? acctUuid = reader["acctUuid"] as string;
                if (mediaUuid is null || fileName is null || acctUuid is null) continue;
                rows.Add((mediaUuid, fileName, acctUuid));
            }
        }
        catch (SqliteException)
        {
            yield break; // schema mismatch — skip attachments rather than fail the whole import
        }

        foreach (var (mediaUuid, fileName, acctUuid) in rows)
        {
            string full = Path.Combine(container, "Accounts", acctUuid, "Media", mediaUuid, fileName);
            yield return (full, MimeTypes.FromExtension(Path.GetExtension(fileName)), $"Media/{mediaUuid}/{fileName}");
        }
    }

    // --- path resolution -------------------------------------------------------------------

    private static string RequireDatabase(string path) =>
        ResolveDatabasePath(path) ?? throw new FileNotFoundException(
            $"Could not find a NoteStore.sqlite database at '{path}'.", path);

    private static string? ResolveDatabasePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            path = DefaultDatabasePath();

        if (File.Exists(path) && Path.GetExtension(path).Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
            return path;

        if (Directory.Exists(path))
        {
            string candidate = Path.Combine(path, "NoteStore.sqlite");
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private static string DefaultDatabasePath()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "Group Containers", "group.com.apple.notes", "NoteStore.sqlite");
    }

    private static SqliteConnection OpenReadOnly(string dbPath) =>
        new(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString());

    // --- value helpers ---------------------------------------------------------------------

    private static DateTimeOffset? FromAppleTime(double? seconds) =>
        seconds is > 0 ? AppleEpoch.AddSeconds(seconds.Value) : null;

    private static string FirstLine(string text)
    {
        int nl = text.IndexOf('\n');
        return (nl >= 0 ? text[..nl] : text).Trim();
    }

    private static bool HasColumn(SqliteDataReader r, string name)
    {
        for (int i = 0; i < r.FieldCount; i++)
            if (string.Equals(r.GetName(i), name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string? GetString(SqliteDataReader r, string name) =>
        HasColumn(r, name) && r[name] is string s ? s : null;

    private static long? GetInt64(SqliteDataReader r, string name) =>
        HasColumn(r, name) && r[name] is long l ? l
        : HasColumn(r, name) && r[name] is double d ? (long)d
        : null;

    private static double? GetDouble(SqliteDataReader r, string name) =>
        HasColumn(r, name) && r[name] is double d ? d
        : HasColumn(r, name) && r[name] is long l ? l
        : null;

    private static byte[]? GetBlob(SqliteDataReader r, string name) =>
        HasColumn(r, name) && r[name] is byte[] b ? b : null;
}
