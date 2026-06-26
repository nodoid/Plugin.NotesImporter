using System.Text.Json.Serialization;

namespace Plugin.NotesImporter.Models;

/// <summary>
/// A single image attached to a <see cref="Note"/>.
/// <para>
/// The raw bytes are held in <see cref="Data"/>. When this object is serialized with
/// <c>System.Text.Json</c> the byte array is emitted as a Base64 string, so a serialized
/// note is fully self-contained (no external files required to round-trip the image).
/// </para>
/// </summary>
public sealed class NoteImage
{
    /// <summary>Original file name of the image, if known (e.g. <c>photo.jpg</c>).</summary>
    public string? FileName { get; set; }

    /// <summary>MIME type of the image (e.g. <c>image/jpeg</c>, <c>image/png</c>).</summary>
    public string MediaType { get; set; } = "application/octet-stream";

    /// <summary>The raw image bytes. Serialized to/from Base64 by System.Text.Json.</summary>
    public byte[] Data { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Where the image came from in the source export (a file path, or a <c>data:</c> URI
    /// marker). Useful for debugging / provenance; not required to render the image.
    /// </summary>
    public string? SourceReference { get; set; }

    /// <summary>Convenience accessor exposing the image as a Base64 string.</summary>
    [JsonIgnore]
    public string Base64 => Convert.ToBase64String(Data);

    /// <summary>Builds a ready-to-use <c>data:</c> URI for embedding in HTML / markdown.</summary>
    [JsonIgnore]
    public string DataUri => $"data:{MediaType};base64,{Base64}";

    /// <summary>Creates a <see cref="NoteImage"/> from a <c>data:</c> URI, or null if it can't be parsed.</summary>
    public static NoteImage? FromDataUri(string dataUri, string? fileName = null)
    {
        // Format: data:[<mediatype>][;base64],<data>
        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return null;

        int comma = dataUri.IndexOf(',');
        if (comma < 0)
            return null;

        string header = dataUri.Substring(5, comma - 5); // strip "data:"
        string payload = dataUri.Substring(comma + 1);

        bool isBase64 = header.Contains(";base64", StringComparison.OrdinalIgnoreCase);
        string mediaType = header.Split(';')[0];
        if (string.IsNullOrWhiteSpace(mediaType))
            mediaType = "application/octet-stream";

        byte[] data;
        try
        {
            data = isBase64
                ? Convert.FromBase64String(payload)
                : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
        }
        catch (FormatException)
        {
            return null;
        }

        return new NoteImage
        {
            FileName = fileName,
            MediaType = mediaType,
            Data = data,
            SourceReference = "data-uri",
        };
    }
}
