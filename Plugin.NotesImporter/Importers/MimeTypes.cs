namespace Plugin.NotesImporter.Importers;

/// <summary>Minimal extension → MIME mapping for the image types notes commonly contain.</summary>
internal static class MimeTypes
{
    public static string FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".tif" or ".tiff" => "image/tiff",
        ".heic" => "image/heic",
        ".heif" => "image/heif",
        ".svg" => "image/svg+xml",
        _ => "application/octet-stream",
    };
}
