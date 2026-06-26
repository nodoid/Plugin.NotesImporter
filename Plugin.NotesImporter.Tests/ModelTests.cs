using Plugin.NotesImporter.Models;
using Xunit;

namespace Plugin.NotesImporter.Tests;

public class NoteImageTests
{
    [Fact]
    public void FromDataUri_parses_base64_png()
    {
        string uri = "data:image/png;base64," + Convert.ToBase64String(TestFixtures.TinyPng);

        var img = NoteImage.FromDataUri(uri, "x.png");

        Assert.NotNull(img);
        Assert.Equal("image/png", img!.MediaType);
        Assert.Equal(TestFixtures.TinyPng, img.Data);
        Assert.Equal("x.png", img.FileName);
    }

    [Fact]
    public void DataUri_round_trips_through_FromDataUri()
    {
        var original = new NoteImage { MediaType = "image/png", Data = TestFixtures.TinyPng };

        var parsed = NoteImage.FromDataUri(original.DataUri);

        Assert.NotNull(parsed);
        Assert.Equal(original.Data, parsed!.Data);
        Assert.Equal(original.MediaType, parsed.MediaType);
    }

    [Theory]
    [InlineData("not-a-data-uri")]
    [InlineData("data:image/png;base64")]   // no comma
    [InlineData("")]
    public void FromDataUri_returns_null_for_invalid_input(string input)
    {
        Assert.Null(NoteImage.FromDataUri(input));
    }

    [Fact]
    public void Base64_matches_data()
    {
        var img = new NoteImage { Data = TestFixtures.TinyPng };
        Assert.Equal(Convert.ToBase64String(TestFixtures.TinyPng), img.Base64);
    }
}

public class NoteSerializationTests
{
    [Fact]
    public void ToJson_FromJson_round_trips_all_fields_including_image_bytes()
    {
        var note = new Note
        {
            Id = "n1",
            Title = "Title",
            PlainText = "Body line 1\nLine 2",
            Source = NoteSource.GoogleKeep,
            CreatedAt = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ModifiedAt = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.Zero),
            IsPinned = true,
            Labels = { "a", "b" },
            Images = { new NoteImage { FileName = "p.png", MediaType = "image/png", Data = TestFixtures.TinyPng } },
            Metadata = { ["color"] = "RED" },
        };

        var restored = Note.FromJson(note.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(note.Id, restored!.Id);
        Assert.Equal(note.Title, restored.Title);
        Assert.Equal(note.PlainText, restored.PlainText);
        Assert.Equal(note.Source, restored.Source);
        Assert.Equal(note.CreatedAt, restored.CreatedAt);
        Assert.Equal(note.ModifiedAt, restored.ModifiedAt);
        Assert.True(restored.IsPinned);
        Assert.Equal(note.Labels, restored.Labels);
        Assert.Single(restored.Images);
        Assert.Equal(TestFixtures.TinyPng, restored.Images[0].Data);
        Assert.Equal("RED", restored.Metadata["color"]);
    }

    [Fact]
    public void Source_enum_serializes_as_string_name()
    {
        var json = new Note { Source = NoteSource.MicrosoftOneNote }.ToJson();
        Assert.Contains("\"MicrosoftOneNote\"", json);
    }

    [Fact]
    public void ToJson_collection_emits_array()
    {
        var json = Note.ToJson(new[] { new Note { Id = "a" }, new Note { Id = "b" } });
        Assert.StartsWith("[", json.TrimStart());
    }
}
