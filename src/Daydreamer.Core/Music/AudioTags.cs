using System.Text;

namespace Daydreamer.Core.Music;

/// <summary>
/// Reads a song's title and artist from the tags stored inside the file — the "ID3" tags that most
/// MP3s carry. Files without tags (and other formats) just go by their file name instead.
/// </summary>
public static class AudioTags
{
    public static (string? Title, string? Artist) Read(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return ReadId3v2(stream) ?? ReadId3v1(stream) ?? (null, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (null, null);
        }
    }

    /// <summary>
    /// ID3 version 2, at the start of the file: a 10-byte header, then "frames" — TIT2 is the title,
    /// TPE1 the artist (TT2 and TP1 in the old version 2.2).
    /// </summary>
    public static (string? Title, string? Artist)? ReadId3v2(Stream stream)
    {
        stream.Position = 0;
        var header = new byte[10];
        if (stream.ReadAtLeast(header, 10, throwOnEndOfStream: false) < 10 || header[0] != 'I' || header[1] != 'D' || header[2] != '3')
            return null;

        int version = header[3];
        byte flags = header[5];
        int size = Synchsafe(header, 6); // the size of everything after the header
        if (version is < 2 or > 4 || size <= 0) return null;

        var tag = new byte[size];
        int read = stream.ReadAtLeast(tag, size, throwOnEndOfStream: false);
        if (read < size) Array.Resize(ref tag, read);
        if ((flags & 0x80) != 0 && version < 4) tag = RemoveUnsynchronisation(tag);

        int position = 0;
        if ((flags & 0x40) != 0 && version >= 3 && tag.Length >= 4) // an optional "extended header" to step over
            position = version == 3 ? 4 + BigEndian(tag, 0) : Synchsafe(tag, 0);

        int idLength = version == 2 ? 3 : 4;
        int frameHeaderLength = version == 2 ? 6 : 10;
        string? title = null, artist = null;
        while (position >= 0 && position + frameHeaderLength <= tag.Length)
        {
            if (tag[position] == 0) break; // the rest is padding
            string id = Encoding.ASCII.GetString(tag, position, idLength);
            int frameSize = version switch
            {
                2 => tag[position + 3] << 16 | tag[position + 4] << 8 | tag[position + 5],
                3 => BigEndian(tag, position + 4),
                _ => Synchsafe(tag, position + 4),
            };
            int dataStart = position + frameHeaderLength;
            if (frameSize <= 0 || dataStart + frameSize > tag.Length) break;

            if (id is "TIT2" or "TT2") title ??= TextFrame(tag, dataStart, frameSize);
            else if (id is "TPE1" or "TP1") artist ??= TextFrame(tag, dataStart, frameSize);
            position = dataStart + frameSize;
        }
        return title == null && artist == null ? null : (title, artist);
    }

    /// <summary>ID3 version 1: a fixed 128-byte block at the very end of the file, starting "TAG".</summary>
    public static (string? Title, string? Artist)? ReadId3v1(Stream stream)
    {
        if (stream.Length < 128) return null;
        stream.Position = stream.Length - 128;
        var tag = new byte[128];
        if (stream.ReadAtLeast(tag, 128, throwOnEndOfStream: false) < 128 || tag[0] != 'T' || tag[1] != 'A' || tag[2] != 'G')
            return null;

        string? Field(int start) => Clean(Encoding.Latin1.GetString(tag, start, 30));
        var (title, artist) = (Field(3), Field(33));
        return title == null && artist == null ? null : (title, artist);
    }

    /// <summary>A text frame: one byte saying how the text is encoded, then the text.</summary>
    private static string? TextFrame(byte[] data, int start, int length)
    {
        if (length < 2) return null;
        byte encodingByte = data[start];
        int offset = start + 1, count = length - 1;
        var encoding = encodingByte switch
        {
            1 => Encoding.Unicode,          // UTF-16, with a byte-order mark saying which way round
            2 => Encoding.BigEndianUnicode, // UTF-16, big-endian, no mark
            3 => Encoding.UTF8,
            _ => Encoding.Latin1,
        };
        if (encodingByte == 1 && count >= 2 && (data[offset], data[offset + 1]) is (0xFF, 0xFE) or (0xFE, 0xFF))
        {
            if (data[offset] == 0xFE) encoding = Encoding.BigEndianUnicode;
            offset += 2;
            count -= 2;
        }
        return Clean(encoding.GetString(data, offset, count));
    }

    /// <summary>The first value, without the zero bytes and spaces tags are padded with.</summary>
    private static string? Clean(string text)
    {
        string first = text.Split('\0')[0].Trim();
        return first.Length == 0 ? null : first;
    }

    /// <summary>Sizes in ID3v2 headers use 7 bits per byte ("synchsafe"), so they never look like audio data.</summary>
    private static int Synchsafe(byte[] b, int i) =>
        (b[i] & 0x7F) << 21 | (b[i + 1] & 0x7F) << 14 | (b[i + 2] & 0x7F) << 7 | (b[i + 3] & 0x7F);

    private static int BigEndian(byte[] b, int i) => b[i] << 24 | b[i + 1] << 16 | b[i + 2] << 8 | b[i + 3];

    /// <summary>Older tags may insert a 0 after every 0xFF byte; this takes them back out.</summary>
    private static byte[] RemoveUnsynchronisation(byte[] data)
    {
        var result = new List<byte>(data.Length);
        for (int i = 0; i < data.Length; i++)
        {
            result.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < data.Length && data[i + 1] == 0) i++;
        }
        return [.. result];
    }
}
