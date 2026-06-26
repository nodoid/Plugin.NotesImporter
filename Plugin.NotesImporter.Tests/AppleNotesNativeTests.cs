using Plugin.NotesImporter;
using Plugin.NotesImporter.Importers;
using Plugin.NotesImporter.Models;
using Xunit;

namespace Plugin.NotesImporter.Tests;

public class AppleNotesProtoTests
{
    [Fact]
    public void ExtractNoteText_decodes_gzipped_protobuf_body()
    {
        byte[] blob = TestFixtures.AppleNoteBlob("Greeting\nHello from Apple Notes");

        string? text = AppleNotesProto.ExtractNoteText(blob);

        Assert.Equal("Greeting\nHello from Apple Notes", text);
    }

    [Fact]
    public void ExtractNoteText_returns_null_for_non_gzip_garbage()
    {
        Assert.Null(AppleNotesProto.ExtractNoteText(new byte[] { 1, 2, 3, 4 }));
    }
}

public class AppleNotesSqliteImporterTests
{
    private static readonly DateTimeOffset Created = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void Reads_note_text_title_pinned_and_created_date_from_database()
    {
        using var dir = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(dir.Path, "Greeting", "Greeting\nHello from Apple Notes", Created, pinned: true);

        var note = new AppleNotesSqliteImporter().Import(dir.Path).Single();

        Assert.Equal(NoteSource.AppleNotes, note.Source);
        Assert.Equal("Greeting", note.Title);
        Assert.True(note.IsPinned);
        Assert.Equal("Greeting\nHello from Apple Notes", note.PlainText);
        Assert.Equal(Created, note.CreatedAt);
        Assert.Equal("NOTE-UUID", note.Id);
    }

    [Fact]
    public void Extracts_attachment_from_media_folder()
    {
        using var dir = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(dir.Path, "T", "body", Created, withAttachment: true);

        var note = new AppleNotesSqliteImporter().Import(dir.Path).Single();

        var img = Assert.Single(note.Images);
        Assert.Equal(TestFixtures.TinyPng, img.Data);
        Assert.Equal("pic.png", img.FileName);
    }

    [Fact]
    public void Note_without_attachment_has_no_images()
    {
        using var dir = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(dir.Path, "T", "body", Created, withAttachment: false);

        var note = new AppleNotesSqliteImporter().Import(dir.Path).Single();

        Assert.Empty(note.Images);
    }

    [Fact]
    public async Task Async_import_matches_sync()
    {
        using var dir = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(dir.Path, "Greeting", "body text", Created, pinned: true);

        var importer = new AppleNotesSqliteImporter();
        var sync = importer.Import(dir.Path).Single();
        var async = (await importer.ImportAsync(dir.Path)).Single();

        Assert.Equal(sync.ToJson(), async.ToJson());
    }

    [Fact]
    public void CanImport_true_for_folder_with_notestore_false_otherwise()
    {
        using var dir = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(dir.Path, "T", "b", Created, withAttachment: false);

        var importer = new AppleNotesSqliteImporter();
        Assert.True(importer.CanImport(dir.Path));

        using var empty = new TestFixtures.TempDir();
        Assert.False(importer.CanImport(empty.Path));
    }
}

public class AutoDetectTests
{
    [Fact]
    public void ImportAuto_routes_keep_html_and_sqlite_to_correct_sources()
    {
        var service = new NotesImportService();

        using var keep = new TestFixtures.TempDir();
        keep.File("n.json", "{\"title\":\"x\",\"isArchived\":false,\"textContent\":\"hi\"}");
        Assert.Equal(NoteSource.GoogleKeep, service.ImportAuto(keep.Path)[0].Source);

        using var html = new TestFixtures.TempDir();
        html.File("n.html", "<html><head><title>t</title></head><body><p>hi</p></body></html>");
        Assert.Equal(NoteSource.AppleNotes, service.ImportAuto(html.Path)[0].Source);

        using var db = new TestFixtures.TempDir();
        TestFixtures.BuildNoteStore(db.Path, "T", "b", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), withAttachment: false);
        Assert.Equal(NoteSource.AppleNotes, service.ImportAuto(db.Path)[0].Source);
    }

    [Fact]
    public void ImportAuto_throws_when_nothing_matches()
    {
        var service = new NotesImportService();
        using var empty = new TestFixtures.TempDir();
        Assert.Throws<InvalidOperationException>(() => service.ImportAuto(empty.Path));
    }
}
