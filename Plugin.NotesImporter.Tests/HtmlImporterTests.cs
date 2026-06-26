using Plugin.NotesImporter.Importers;
using Plugin.NotesImporter.Models;
using Xunit;

namespace Plugin.NotesImporter.Tests;

public class HtmlImporterTests
{
    private static string Html(string body, string? title = "Trip ideas", string? created = "2024-05-01T09:30:00Z") =>
        $$"""
        <!DOCTYPE html>
        <html><head>
        {{(title is null ? "" : $"<title>{title}</title>")}}
        {{(created is null ? "" : $"<meta name=\"created\" content=\"{created}\">")}}
        </head><body>{{body}}</body></html>
        """;

    [Fact]
    public void Extracts_title_text_and_meta_created_date()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note.html", Html("<h1>Trip ideas</h1><p>Visit the <b>coast</b>.</p><p>Second.</p>"));

        var note = new AppleNotesImporter().Import(dir.Path).Single();

        Assert.Equal(NoteSource.AppleNotes, note.Source);
        Assert.Equal("Trip ideas", note.Title);
        Assert.Contains("Visit the coast.", note.PlainText);
        Assert.Contains("Second.", note.PlainText);
        Assert.Equal(new DateTimeOffset(2024, 5, 1, 9, 30, 0, TimeSpan.Zero), note.CreatedAt);
        Assert.NotNull(note.Html);
    }

    [Fact]
    public void Extracts_inline_data_uri_image()
    {
        using var dir = new TestFixtures.TempDir();
        string uri = "data:image/png;base64," + Convert.ToBase64String(TestFixtures.TinyPng);
        dir.File("note.html", Html($"<p>x</p><img src=\"{uri}\" />"));

        var note = new AppleNotesImporter().Import(dir.Path).Single();

        var img = Assert.Single(note.Images);
        Assert.Equal("image/png", img.MediaType);
        Assert.Equal(TestFixtures.TinyPng, img.Data);
    }

    [Fact]
    public void Extracts_linked_image_file_next_to_html()
    {
        using var dir = new TestFixtures.TempDir();
        dir.Bytes("assets/pic.png", TestFixtures.TinyPng);
        dir.File("note.html", Html("<img src=\"assets/pic.png\" />"));

        var note = new AppleNotesImporter().Import(dir.Path).Single();

        var img = Assert.Single(note.Images);
        Assert.Equal(TestFixtures.TinyPng, img.Data);
        Assert.Equal("pic.png", img.FileName);
    }

    [Fact]
    public void Skips_remote_images()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note.html", Html("<img src=\"https://example.com/x.png\" />"));

        var note = new AppleNotesImporter().Import(dir.Path).Single();

        Assert.Empty(note.Images);
    }

    [Fact]
    public void Falls_back_to_file_timestamp_when_no_meta_date()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("note.html", Html("<p>hi</p>", created: null));

        var note = new AppleNotesImporter().Import(dir.Path).Single();

        Assert.NotNull(note.CreatedAt); // file creation time
    }

    [Fact]
    public async Task Async_import_matches_sync()
    {
        using var dir = new TestFixtures.TempDir();
        dir.Bytes("pic.png", TestFixtures.TinyPng);
        dir.File("note.html", Html("<h1>T</h1><p>body</p><img src=\"pic.png\"/>"));

        var importer = new AppleNotesImporter();
        var sync = importer.Import(dir.Path).Single();
        var async = (await importer.ImportAsync(dir.Path)).Single();

        Assert.Equal(sync.ToJson(), async.ToJson());
    }

    [Fact]
    public void OneNote_importer_reads_html_with_onenote_source()
    {
        using var dir = new TestFixtures.TempDir();
        dir.File("page.html", Html("<h1>Page</h1><p>content</p>", title: "Page"));

        var note = new MicrosoftOneNoteImporter().Import(dir.Path).Single();

        Assert.Equal(NoteSource.MicrosoftOneNote, note.Source);
        Assert.Equal("Page", note.Title);
    }
}
