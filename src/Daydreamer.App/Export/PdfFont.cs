using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace Daydreamer.App.Export;

/// <summary>
/// A font packed into a PDF. Only the letters (glyphs) the document uses go in — WPF's own font subsetter
/// cuts them out, keeping each glyph's number, so the text can refer to glyphs by number directly. Alongside
/// go each glyph's width and a table of which characters it stands for, so text copied out of the PDF (or
/// searched, or read aloud) comes back as the real words.
/// </summary>
internal sealed class PdfFont
{
    // The parts of a font file a PDF needs. The rest (kerning and layout rules, extra hinting data) only matters
    // while laying text out, which is already done — leaving them out makes files several times smaller.
    private static readonly HashSet<string> TablesToKeep =
        ["cmap", "cvt ", "fpgm", "glyf", "head", "hhea", "hmtx", "loca", "maxp", "name", "OS/2", "post", "prep"];

    // Glyph number → its width (in 1000ths of the font size) and the characters it stands for, if known.
    private readonly SortedDictionary<ushort, (int Width, string? Text)> _glyphs = [];

    private PdfFont(GlyphTypeface typeface, string resourceName, int objectNumber)
    {
        Typeface = typeface;
        ResourceName = resourceName;
        ObjectNumber = objectNumber;
    }

    public GlyphTypeface Typeface { get; }

    /// <summary>The name page content uses for this font, like "F1".</summary>
    public string ResourceName { get; }

    /// <summary>The PDF object that is this font.</summary>
    public int ObjectNumber { get; }

    /// <summary>
    /// A font ready to pack in, or null if this one can't be: its licence doesn't allow it, or it isn't the
    /// TrueType kind of font this writer knows how to pack. Text in such a font is drawn as shapes instead.
    /// </summary>
    public static PdfFont? TryCreate(GlyphTypeface typeface, string resourceName, int objectNumber)
    {
        if (typeface.EmbeddingRights == FontEmbeddingRight.RestrictedLicense) return null;
        try
        {
            // TrueType fonts start with 00 01 00 00 or "true", collections (.ttc) with "ttcf". "OTTO" is the
            // other kind of OpenType font (with PostScript outlines), which WPF's subsetter can't cut down.
            using var stream = typeface.GetFontStream();
            var start = new byte[4];
            if (stream.Read(start, 0, 4) < 4) return null;
            string tag = Encoding.ASCII.GetString(start);
            bool trueType = start is [0, 1, 0, 0] || tag is "true" or "ttcf";
            return trueType ? new PdfFont(typeface, resourceName, objectNumber) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Notes that the document uses a glyph, and the characters it stands for (null if not known).</summary>
    public void Use(ushort glyph, string? text)
    {
        if (!_glyphs.TryGetValue(glyph, out var known))
        {
            double advance = Typeface.AdvanceWidths.TryGetValue(glyph, out double width) ? width : 0;
            _glyphs[glyph] = ((int)Math.Round(advance * 1000), text);
        }
        else if (known.Text == null && !string.IsNullOrEmpty(text))
        {
            _glyphs[glyph] = known with { Text = text };
        }
    }

    /// <summary>The width a PDF viewer will move along after drawing this glyph, in 1000ths of the font size.</summary>
    public int WidthOf(ushort glyph) => _glyphs.TryGetValue(glyph, out var g) ? g.Width : 0;

    /// <summary>
    /// Writes the font's objects. PDF describes a font like this in five parts: the font as text refers to it
    /// (a "Type 0" font that reads glyph numbers), the glyph widths, a description (size, slant, weight),
    /// the font file itself, and the glyph → characters table.
    /// </summary>
    public void Write(PdfFile pdf, int index)
    {
        Use(0, null); // glyph 0 is the "missing letter" box, which every font must have

        byte[] subset = KeepTables(Typeface.ComputeSubset(_glyphs.Keys.ToList()), TablesToKeep);
        string name = $"{SubsetTag(index)}+{PostScriptName()}";
        int descendant = pdf.NewObject(), descriptor = pdf.NewObject(), file = pdf.NewObject(), toUnicode = pdf.NewObject();

        pdf.WriteObject(ObjectNumber,
            $"<< /Type /Font /Subtype /Type0 /BaseFont /{name} /Encoding /Identity-H /DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>");

        // Text gives glyph numbers directly ("Identity"): code 0x0012 is glyph 18 in the font file.
        pdf.WriteObject(descendant,
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{name} /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptor} 0 R /CIDToGIDMap /Identity /W [{Widths()}] >>");

        bool italic = Typeface.Style != FontStyles.Normal;
        int flags = (Typeface.Symbol ? 4 : 32) | (italic ? 64 : 0);
        var box = BoundingBox(subset);
        pdf.WriteObject(descriptor,
            $"<< /Type /FontDescriptor /FontName /{name} /Flags {flags} /FontBBox [{box}] /ItalicAngle {(italic ? -12 : 0)} " +
            $"/Ascent {Thousandths(Typeface.Baseline)} /Descent {Thousandths(Typeface.Baseline - Typeface.Height)} " +
            $"/CapHeight {Thousandths(Typeface.CapsHeight)} /StemV {(Typeface.Weight.ToOpenTypeWeight() >= 600 ? 120 : 80)} /FontFile2 {file} 0 R >>");

        pdf.WriteStream(file, $"/Length1 {subset.Length}", subset);
        pdf.WriteStream(toUnicode, "", Encoding.ASCII.GetBytes(ToUnicodeMap()));
    }

    /// <summary>Widths of runs of neighbouring glyphs: "18 [500 620] 40 [250]".</summary>
    private string Widths()
    {
        var widths = new StringBuilder();
        int previous = -2;
        foreach (var (glyph, (width, _)) in _glyphs)
        {
            if (glyph != previous + 1)
            {
                if (previous >= 0) widths.Append("] ");
                widths.Append(glyph).Append(" [");
            }
            else
            {
                widths.Append(' ');
            }
            widths.Append(width);
            previous = glyph;
        }
        if (previous >= 0) widths.Append(']');
        return widths.ToString();
    }

    /// <summary>The glyph → characters table, in the little PostScript-style language PDF uses for it (a "CMap").</summary>
    private string ToUnicodeMap()
    {
        var entries = _glyphs.Where(g => !string.IsNullOrEmpty(g.Value.Text))
            .Select(g => $"<{g.Key:X4}> <{string.Concat(g.Value.Text!.Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture)))}>")
            .ToList();

        var map = new StringBuilder();
        map.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        map.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        map.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        map.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        // At most 100 entries per block.
        foreach (var block in entries.Chunk(100))
        {
            map.Append(block.Length).Append(" beginbfchar\n");
            foreach (string entry in block) map.Append(entry).Append('\n');
            map.Append("endbfchar\n");
        }
        map.Append("endcmap\nCMapName currentdict /CIDInit defineresource pop\nend\nend\n");
        return map.ToString();
    }

    /// <summary>
    /// A packed-in font's name starts with six capital letters and a +, which says "this is only part of the
    /// font" and keeps two different parts of the same font apart: AAAAAB+SegoeUI-Bold.
    /// </summary>
    private static string SubsetTag(int index)
    {
        var tag = new char[6];
        for (int i = 5; i >= 0; i--, index /= 26) tag[i] = (char)('A' + index % 26);
        return new string(tag);
    }

    /// <summary>"Segoe UI" Bold → "SegoeUI-Bold". PDF names can't have spaces, so only letters and digits are kept.</summary>
    private string PostScriptName()
    {
        static string Clean(string? text) => new((text ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
        string family = Clean(English(Typeface.Win32FamilyNames));
        string face = Clean(English(Typeface.Win32FaceNames));
        if (family.Length == 0) family = "Font";
        return face.Length == 0 ? family : $"{family}-{face}";
    }

    private static string? English(IDictionary<CultureInfo, string> names) =>
        names.TryGetValue(CultureInfo.GetCultureInfo("en-US"), out string? name) ? name : names.Values.FirstOrDefault();

    private static int Thousandths(double emFraction) => (int)Math.Round(emFraction * 1000);

    // ── Reading and rewriting the font file ────────────────────────────────
    // A TrueType file starts with a directory of "tables" (named by four letters, like "glyf" for the glyph
    // shapes), each with a checksum, position and length; the tables follow. All numbers are big-endian.

    private static int U16(byte[] data, int offset) => data[offset] << 8 | data[offset + 1];

    private static uint U32(byte[] data, int offset) =>
        (uint)(data[offset] << 24 | data[offset + 1] << 16 | data[offset + 2] << 8 | data[offset + 3]);

    private static void PutU16(byte[] data, int offset, int value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void PutU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
    }

    private static Dictionary<string, (int Offset, int Length)> TablesIn(byte[] font)
    {
        var tables = new Dictionary<string, (int, int)>();
        for (int i = 0; i < U16(font, 4); i++)
        {
            int record = 12 + i * 16;
            tables[Encoding.ASCII.GetString(font, record, 4)] = ((int)U32(font, record + 8), (int)U32(font, record + 12));
        }
        return tables;
    }

    /// <summary>The smallest box every glyph fits in, from the "head" table, in 1000ths of the font size.</summary>
    private static string BoundingBox(byte[] font)
    {
        if (!TablesIn(font).TryGetValue("head", out var head)) return "0 0 1000 1000";
        double unitsPerEm = U16(font, head.Offset + 18);
        int Scaled(int offset) => (int)Math.Round((short)U16(font, head.Offset + offset) * 1000 / unitsPerEm);
        return $"{Scaled(36)} {Scaled(38)} {Scaled(40)} {Scaled(42)}";
    }

    /// <summary>A copy of a font file with only the given tables, its directory and checksums rebuilt.</summary>
    private static byte[] KeepTables(byte[] font, HashSet<string> keep)
    {
        var kept = TablesIn(font).Where(t => keep.Contains(t.Key)).OrderBy(t => t.Key, StringComparer.Ordinal).ToList();
        int count = kept.Count;
        int dataStart = 12 + count * 16;
        int size = dataStart + kept.Sum(t => (t.Value.Length + 3) & ~3); // each table padded to 4 bytes
        var output = new byte[size];

        // The header: the font kind, the number of tables, and three numbers that help readers search the directory.
        Array.Copy(font, output, 4);
        int power = 1, log = 0;
        while (power * 2 <= count) { power *= 2; log++; }
        PutU16(output, 4, count);
        PutU16(output, 6, power * 16);
        PutU16(output, 8, log);
        PutU16(output, 10, count * 16 - power * 16);

        int position = dataStart;
        int headAt = -1;
        for (int i = 0; i < count; i++)
        {
            var (tag, (offset, length)) = kept[i];
            Array.Copy(font, offset, output, position, length);
            if (tag == "head")
            {
                headAt = position;
                PutU32(output, position + 8, 0); // the whole-file checksum adjustment, worked out below
            }
            int record = 12 + i * 16;
            Encoding.ASCII.GetBytes(tag, 0, 4, output, record);
            PutU32(output, record + 4, Checksum(output, position, length));
            PutU32(output, record + 8, (uint)position);
            PutU32(output, record + 12, (uint)length);
            position += (length + 3) & ~3;
        }
        if (headAt >= 0) PutU32(output, headAt + 8, 0xB1B0AFBA - Checksum(output, 0, output.Length));
        return output;
    }

    /// <summary>A TrueType checksum: the sum of the data read as 4-byte numbers.</summary>
    private static uint Checksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        for (int i = 0; i < length; i += 4)
        {
            uint word = 0;
            for (int b = 0; b < 4; b++)
                word = word << 8 | (uint)(i + b < length ? data[offset + i + b] : 0);
            sum += word;
        }
        return sum;
    }
}
