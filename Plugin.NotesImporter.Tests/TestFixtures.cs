using System.IO.Compression;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Plugin.NotesImporter.Tests;

/// <summary>Helpers for building throwaway, on-disk fixtures (folders, Keep JSON, HTML, NoteStore.sqlite).</summary>
internal static class TestFixtures
{
    /// <summary>A 1×1 PNG used as a stand-in attachment (70 bytes).</summary>
    public static byte[] TinyPng => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    /// <summary>A disposable temp directory that deletes itself.</summary>
    public sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "notesimporter-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string File(string name, string contents)
        {
            string full = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, contents);
            return full;
        }

        public string Bytes(string name, byte[] contents)
        {
            string full = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllBytes(full, contents);
            return full;
        }

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }

    // --- protobuf / gzip (for Apple Notes body) -----------------------------------------

    /// <summary>Encodes a length-delimited protobuf field (wire type 2).</summary>
    public static byte[] ProtoField(int number, byte[] payload)
    {
        using var ms = new MemoryStream();
        WriteVarint(ms, (ulong)((number << 3) | 2));
        WriteVarint(ms, (ulong)payload.Length);
        ms.Write(payload);
        return ms.ToArray();
    }

    /// <summary>Builds the gzipped ZDATA blob Apple Notes stores for a note with the given text.</summary>
    public static byte[] AppleNoteBlob(string text)
    {
        byte[] proto = ProtoField(2, ProtoField(3, ProtoField(2, Encoding.UTF8.GetBytes(text))));
        return Gzip(proto);
    }

    private static void WriteVarint(Stream s, ulong v)
    {
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            s.WriteByte(b);
        } while (v != 0);
    }

    public static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(data);
        return ms.ToArray();
    }

    public static double AppleSeconds(DateTimeOffset when) =>
        (when - new DateTimeOffset(2001, 1, 1, 0, 0, 0, TimeSpan.Zero)).TotalSeconds;

    /// <summary>
    /// Creates a minimal but realistic NoteStore.sqlite under <paramref name="dir"/>, with one note
    /// (optionally pinned) and one image attachment laid out under Accounts/&lt;acct&gt;/Media/...
    /// Returns the directory path (pass it to the importer).
    /// </summary>
    public static string BuildNoteStore(string dir, string title, string bodyText,
        DateTimeOffset created, bool pinned = false, bool withAttachment = true)
    {
        Directory.CreateDirectory(dir);
        string dbPath = Path.Combine(dir, "NoteStore.sqlite");
        if (File.Exists(dbPath)) File.Delete(dbPath);

        const string acct = "ACCT-UUID", media = "MEDIA-UUID", fileName = "pic.png";
        if (withAttachment)
        {
            string mediaDir = Path.Combine(dir, "Accounts", acct, "Media", media);
            Directory.CreateDirectory(mediaDir);
            File.WriteAllBytes(Path.Combine(mediaDir, fileName), TinyPng);
        }

        double secs = AppleSeconds(created);

        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        Exec(conn, @"CREATE TABLE ZICCLOUDSYNCINGOBJECT(
            Z_PK INTEGER PRIMARY KEY, ZNOTEDATA INTEGER, ZTITLE1 TEXT, ZSNIPPET TEXT,
            ZCREATIONDATE1 REAL, ZMODIFICATIONDATE1 REAL, ZIDENTIFIER TEXT, ZISPINNED INTEGER,
            ZMARKEDFORDELETION INTEGER, ZNOTE INTEGER, ZMEDIA INTEGER, ZFILENAME TEXT, ZACCOUNT INTEGER)");
        Exec(conn, "CREATE TABLE ZICNOTEDATA(Z_PK INTEGER PRIMARY KEY, ZDATA BLOB)");

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "INSERT INTO ZICNOTEDATA(Z_PK, ZDATA) VALUES (1, $d)";
            cmd.Parameters.AddWithValue("$d", AppleNoteBlob(bodyText));
            cmd.ExecuteNonQuery();
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText =
                "INSERT INTO ZICCLOUDSYNCINGOBJECT(Z_PK, ZNOTEDATA, ZTITLE1, ZCREATIONDATE1, ZMODIFICATIONDATE1, ZIDENTIFIER, ZISPINNED, ZMARKEDFORDELETION) " +
                "VALUES (10, 1, $t, $c, $c, 'NOTE-UUID', $p, 0)";
            cmd.Parameters.AddWithValue("$t", title);
            cmd.Parameters.AddWithValue("$c", secs);
            cmd.Parameters.AddWithValue("$p", pinned ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        if (withAttachment)
        {
            Exec(conn, $"INSERT INTO ZICCLOUDSYNCINGOBJECT(Z_PK, ZIDENTIFIER) VALUES (30, '{acct}')");
            Exec(conn, $"INSERT INTO ZICCLOUDSYNCINGOBJECT(Z_PK, ZIDENTIFIER, ZFILENAME, ZACCOUNT) VALUES (20, '{media}', '{fileName}', 30)");
            Exec(conn, "INSERT INTO ZICCLOUDSYNCINGOBJECT(Z_PK, ZNOTE, ZMEDIA) VALUES (40, 10, 20)");
        }

        return dir;
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
