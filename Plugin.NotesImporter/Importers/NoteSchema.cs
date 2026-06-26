using Microsoft.Data.Sqlite;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Probes a <c>NoteStore.sqlite</c> at runtime and builds the SELECT query for notes. Apple renames
/// columns between OS releases (e.g. <c>ZTITLE1</c> vs <c>ZTITLE</c>, <c>ZCREATIONDATE1/2/3</c>), so
/// the importer discovers which columns exist rather than hard-coding one schema version.
/// </summary>
internal sealed class NoteSchema
{
    public required string NotesQuery { get; init; }
    public required bool SupportsAttachments { get; init; }

    public static NoteSchema Discover(SqliteConnection conn)
    {
        var columns = ReadColumns(conn, "ZICCLOUDSYNCINGOBJECT");

        if (!columns.Contains("ZNOTEDATA"))
            throw new InvalidOperationException(
                "This does not look like an Apple Notes database (no ZNOTEDATA column on ZICCLOUDSYNCINGOBJECT).");

        string ident = Pick(columns, "ZIDENTIFIER");
        string title = Pick(columns, "ZTITLE1", "ZTITLE2", "ZTITLE");
        string created = Pick(columns, "ZCREATIONDATE3", "ZCREATIONDATE1", "ZCREATIONDATE2", "ZCREATIONDATE");
        string modified = Pick(columns, "ZMODIFICATIONDATE1", "ZMODIFICATIONDATE2", "ZMODIFICATIONDATE3", "ZMODIFICATIONDATE");
        string pinned = Pick(columns, "ZISPINNED");
        string deleted = Pick(columns, "ZMARKEDFORDELETION");
        string snippet = Pick(columns, "ZSNIPPET");

        string query =
            "SELECT n.Z_PK AS pk, " +
            $"{Col(ident, "ident")}, " +
            $"{Col(title, "title")}, " +
            $"{Col(created, "created")}, " +
            $"{Col(modified, "modified")}, " +
            $"{Col(pinned, "pinned")}, " +
            $"{Col(deleted, "deleted")}, " +
            $"{Col(snippet, "snippet")}, " +
            "d.ZDATA AS data " +
            "FROM ZICCLOUDSYNCINGOBJECT n " +
            "JOIN ZICNOTEDATA d ON n.ZNOTEDATA = d.Z_PK " +
            "WHERE n.ZNOTEDATA IS NOT NULL";

        bool attachments = columns.Contains("ZNOTE") && columns.Contains("ZMEDIA");

        return new NoteSchema { NotesQuery = query, SupportsAttachments = attachments };
    }

    private static HashSet<string> ReadColumns(SqliteConnection conn, string table)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            set.Add(reader.GetString(1)); // column 1 = name
        return set;
    }

    private static string Pick(HashSet<string> columns, params string[] candidates) =>
        candidates.FirstOrDefault(columns.Contains) ?? string.Empty;

    /// <summary>Emits <c>n.COLUMN AS alias</c>, or <c>NULL AS alias</c> when the column is absent.</summary>
    private static string Col(string column, string alias) =>
        string.IsNullOrEmpty(column) ? $"NULL AS {alias}" : $"n.{column} AS {alias}";
}
