using System.IO.Compression;
using System.Text;

namespace Plugin.NotesImporter.Importers;

/// <summary>
/// Decodes the body of an Apple Notes note. In <c>NoteStore.sqlite</c> each note's content is
/// stored in <c>ZICNOTEDATA.ZDATA</c> as a gzip-compressed protobuf message. The plain-text body
/// lives at <c>NoteStoreProto.document(2).note(3).note_text(2)</c>.
/// <para>
/// Rather than take a protobuf dependency, this walks the protobuf wire format directly — it only
/// needs to follow three nested length-delimited fields to reach the UTF-8 text.
/// </para>
/// </summary>
internal static class AppleNotesProto
{
    /// <summary>Extracts the plain-text body from a gzipped ZDATA blob, or null if it can't be decoded.</summary>
    public static string? ExtractNoteText(byte[] gzipped)
    {
        byte[] data;
        try
        {
            data = Gunzip(gzipped);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        // NoteStoreProto.document = field 2  ->  Document.note = field 3  ->  Note.note_text = field 2
        var document = FindLengthDelimited(data, 2);
        if (document == null) return null;

        var note = FindLengthDelimited(document, 3);
        if (note == null) return null;

        var textBytes = FindLengthDelimited(note, 2);
        if (textBytes == null) return null;

        return Encoding.UTF8.GetString(textBytes);
    }

    private static byte[] Gunzip(byte[] gzipped)
    {
        using var input = new MemoryStream(gzipped);
        using var gz = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gz.CopyTo(output);
        return output.ToArray();
    }

    /// <summary>Returns the bytes of the first length-delimited (wire type 2) field with the given number.</summary>
    private static byte[]? FindLengthDelimited(byte[] data, int fieldNumber)
    {
        int pos = 0;
        while (pos < data.Length)
        {
            if (!TryReadVarint(data, ref pos, out ulong key)) return null;
            int field = (int)(key >> 3);
            int wireType = (int)(key & 0x7);

            switch (wireType)
            {
                case 0: // varint
                    if (!TryReadVarint(data, ref pos, out _)) return null;
                    break;
                case 1: // 64-bit
                    pos += 8;
                    break;
                case 5: // 32-bit
                    pos += 4;
                    break;
                case 2: // length-delimited
                    if (!TryReadVarint(data, ref pos, out ulong len)) return null;
                    int length = (int)len;
                    if (length < 0 || pos + length > data.Length) return null;
                    if (field == fieldNumber)
                    {
                        var slice = new byte[length];
                        Array.Copy(data, pos, slice, 0, length);
                        return slice;
                    }
                    pos += length;
                    break;
                default:
                    return null; // groups / unknown wire types: bail out
            }
        }

        return null;
    }

    private static bool TryReadVarint(byte[] data, ref int pos, out ulong value)
    {
        value = 0;
        int shift = 0;
        while (pos < data.Length && shift < 64)
        {
            byte b = data[pos++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) return true;
            shift += 7;
        }

        return false; // truncated varint
    }
}
