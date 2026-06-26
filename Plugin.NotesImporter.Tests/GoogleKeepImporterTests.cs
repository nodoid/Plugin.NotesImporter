using Plugin.NotesImporter.Importers;
using Plugin.NotesImporter.Models;
using Xunit;

namespace Plugin.NotesImporter.Tests;

public class GoogleKeepImporterTests
{
    private const string NoteJson = """
    {
      "color": "RED",
      "isTrashed": false,
      "isPinned": true,
      "isArchived": false,
      "title": "Shopping list",
      "textContent": "Remember the milk",
      "listContent": [
        {"text": "Bread", "isChecked": false},
        {"text": "Eggs", "isChecked": true}
      ],
      "labels": [{"name": "Errands"}],
      "attachments": [{"filePath": "photo.png", "mimetype": "image/png"}],
      "createdTimestampUsec": 1552332433953000,
      "userEditedTimestampUsec": 1552332530467000
    }
    """;

    [Fact]
    public void Parses_title_text_checklist_labels_flags_and_timestamps()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note1.json", NoteJson);
        dir.Bytes("photo.png", TestFixtures.TinyPng);

        var note = new GoogleKeepImporter().Import(dir.Path).Single();

        Assert.Equal(NoteSource.GoogleKeep, note.Source);
        Assert.Equal("Shopping list", note.Title);
        Assert.True(note.IsPinned);
        Assert.Contains("Remember the milk", note.PlainText);
        Assert.Contains("[ ] Bread", note.PlainText);
        Assert.Contains("[x] Eggs", note.PlainText);
        Assert.Equal(new[] { "Errands" }, note.Labels);
        Assert.Equal("RED", note.Metadata["color"]);

        // 1552332433953000 µs == 2019-03-11T19:27:13.953Z
        Assert.Equal(new DateTimeOffset(2019, 3, 11, 19, 27, 13, 953, TimeSpan.Zero), note.CreatedAt);
    }

    [Fact]
    public void Embeds_attachment_bytes()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note1.json", NoteJson);
        dir.Bytes("photo.png", TestFixtures.TinyPng);

        var note = new GoogleKeepImporter().Import(dir.Path).Single();

        var img = Assert.Single(note.Images);
        Assert.Equal("image/png", img.MediaType);
        Assert.Equal(TestFixtures.TinyPng, img.Data);
    }

    [Fact]
    public void Records_missing_attachment_in_metadata()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note1.json", NoteJson); // photo.png intentionally absent

        var note = new GoogleKeepImporter().Import(dir.Path).Single();

        Assert.Empty(note.Images);
        Assert.Contains(note.Metadata.Keys, k => k.Contains("missingAttachment"));
    }

    [Fact]
    public async Task Async_import_matches_sync()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note1.json", NoteJson);
        dir.Bytes("photo.png", TestFixtures.TinyPng);

        var importer = new GoogleKeepImporter();
        var sync = importer.Import(dir.Path).Single();
        var async = (await importer.ImportAsync(dir.Path)).Single();

        Assert.Equal(sync.ToJson(), async.ToJson());
    }

    [Fact]
    public void CanImport_detects_keep_notes_and_rejects_other_json()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note1.json", NoteJson);
        Assert.True(new GoogleKeepImporter().CanImport(dir.Path));

        using var other = new TestFixtures.TempDir();
        other.File("random.json", "{\"foo\": 1}");
        Assert.False(new GoogleKeepImporter().CanImport(other.Path));
    }
}
