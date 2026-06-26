using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plugin.NotesImporter.Models;

/// <summary>
/// A single note in a unified, service-agnostic shape. This is the data object the library
/// produces regardless of whether the note originated in Apple Notes, Google Keep or OneNote.
/// It serializes cleanly to JSON, with images embedded as Base64 (see <see cref="NoteImage"/>).
/// </summary>
public sealed class Note
{
    /// <summary>A stable identifier for the note within its source (export id, file name, etc.).</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>The note title, if the source provides one.</summary>
    public string? Title { get; set; }

    /// <summary>The note body as plain text (markup stripped). Always populated when possible.</summary>
    public string PlainText { get; set; } = string.Empty;

    /// <summary>The original HTML body, when the source is HTML-based. Null for plain sources.</summary>
    public string? Html { get; set; }

    /// <summary>Images attached to the note, with bytes embedded for self-contained serialization.</summary>
    public List<NoteImage> Images { get; set; } = new();

    /// <summary>Labels / tags / folder names associated with the note.</summary>
    public List<string> Labels { get; set; } = new();

    /// <summary>When the note was created, if the source records it.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>When the note was last modified, if the source records it.</summary>
    public DateTimeOffset? ModifiedAt { get; set; }

    /// <summary>Which service this note came from.</summary>
    public NoteSource Source { get; set; } = NoteSource.Unknown;

    public bool IsPinned { get; set; }
    public bool IsArchived { get; set; }
    public bool IsTrashed { get; set; }

    /// <summary>Any extra source-specific fields preserved verbatim (color, raw timestamps, etc.).</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes this note (images included as Base64) to JSON.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>Deserializes a note from JSON produced by <see cref="ToJson"/>.</summary>
    public static Note? FromJson(string json) => JsonSerializer.Deserialize<Note>(json, JsonOptions);

    /// <summary>Serializes a collection of notes to a single JSON array (images included).</summary>
    public static string ToJson(IEnumerable<Note> notes) => JsonSerializer.Serialize(notes, JsonOptions);
}
