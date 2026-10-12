using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using Daydreamer.Core.Export;

namespace Daydreamer.App.Export;

/// <summary>
/// Makes a PDF of an exported document. <see cref="PageLayout"/> draws each page with WPF; this turns those
/// drawings into PDF's own drawing instructions. Text stays real text — you can select, search and copy it —
/// and the fonts are packed inside (see <see cref="PdfFont"/>), so the file looks the same on every computer.
/// </summary>
public sealed class PdfWriter
{
    // WPF measures in 1/96 inch with y going down the page; PDF in points (1/72 inch) with y going up.
    // Each page's instructions start by switching to WPF's way, so everything after uses WPF's numbers as they are.
    private const double PointsPerPixel = 72.0 / 96.0;

    // How WPF fakes styles a font doesn't have (measured from WPF's own output): italic leans letters over by
    // 0.34 of their height, and bold thickens them by 2% of the font size.
    private const double SimulatedItalicSlant = 0.34;
    private const double SimulatedBoldStroke = 0.02;

    private readonly PdfFile _pdf = new();
    private readonly Dictionary<string, PdfFont?> _fonts = [];
    private readonly int _resources;

    private PdfWriter()
    {
        _resources = _pdf.NewObject();
    }

    /// <summary>Lays the document out on pages and returns the PDF file's bytes.</summary>
    public static byte[] Export(ExportDocument document, PaperSize paper) =>
        Write(PageLayout.Paginate(document, paper), paper, document.Title);

    public static byte[] Write(IReadOnlyList<Drawing> pages, PaperSize paper, string title)
    {
        var writer = new PdfWriter();
        var pdf = writer._pdf;
        int catalog = pdf.NewObject(), pageTree = pdf.NewObject(), info = pdf.NewObject();

        var pageObjects = new List<int>();
        foreach (var page in pages)
        {
            var content = new StringBuilder();
            content.Append(Invariant($"q {PointsPerPixel} 0 0 {-PointsPerPixel} 0 {paper.Height} cm\n"));
            writer.Draw(page, content);
            content.Append("Q\n");

            int contents = pdf.NewObject(), pageObject = pdf.NewObject();
            pdf.WriteStream(contents, "", Encoding.ASCII.GetBytes(content.ToString()));
            pdf.WriteObject(pageObject, Invariant(
                $"<< /Type /Page /Parent {pageTree} 0 R /MediaBox [0 0 {paper.Width} {paper.Height}] /Resources {writer._resources} 0 R /Contents {contents} 0 R >>"));
            pageObjects.Add(pageObject);
        }

        // Now that every page is written, each font knows all the letters it needs.
        var fonts = writer._fonts.Values.OfType<PdfFont>().ToList();
        for (int i = 0; i < fonts.Count; i++) fonts[i].Write(pdf, i);
        pdf.WriteObject(writer._resources,
            $"<< /Font << {string.Join(" ", fonts.Select(f => $"/{f.ResourceName} {f.ObjectNumber} 0 R"))} >> >>");

        pdf.WriteObject(pageTree, $"<< /Type /Pages /Kids [{string.Join(" ", pageObjects.Select(p => $"{p} 0 R"))}] /Count {pageObjects.Count} >>");
        pdf.WriteObject(info, $"<< /Title {PdfFile.TextString(title)} /Producer (Daydreamer) /CreationDate {PdfFile.Date(DateTimeOffset.Now)} >>");
        // DisplayDocTitle: PDF viewers show the document's title rather than its file name.
        pdf.WriteObject(catalog, $"<< /Type /Catalog /Pages {pageTree} 0 R /ViewerPreferences << /DisplayDocTitle true >> >>");
        return pdf.Finish(root: catalog, info: info);
    }

    // ── Turning WPF drawings into PDF instructions ─────────────────────────

    private void Draw(Drawing drawing, StringBuilder content)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                var transform = group.Transform?.Value ?? Matrix.Identity;
                if (!transform.IsIdentity) content.Append("q ").Append(MatrixText(transform)).Append(" cm\n");
                foreach (var child in group.Children) Draw(child, content);
                if (!transform.IsIdentity) content.Append("Q\n");
                break;

            case GlyphRunDrawing { GlyphRun: { } run } text:
                DrawText(run, ColorOf(text.ForegroundBrush), content);
                break;

            case GeometryDrawing { Geometry: { } geometry } shape:
                DrawShape(geometry, shape.Brush, shape.Pen, content);
                break;
        }
    }

    /// <summary>
    /// A run of glyphs in one font, size and color. PDF moves along by each glyph's own width after drawing
    /// it; wherever WPF placed the next glyph somewhere else (kerning, justified text), a small correction
    /// in the TJ list moves it there.
    /// </summary>
    private void DrawText(GlyphRun run, Color color, StringBuilder content)
    {
        var font = FontFor(run.GlyphTypeface);
        if (font == null)
        {
            // A font that can't be packed in: draw the letters' outlines instead, so they still look right.
            DrawShape(run.BuildGeometry(), new SolidColorBrush(color), null, content);
            return;
        }
        RememberCharacters(run, font);

        double size = run.FontRenderingEmSize;
        var simulations = run.GlyphTypeface.StyleSimulations;
        double slant = simulations.HasFlag(StyleSimulations.ItalicSimulation) ? SimulatedItalicSlant : 0;
        bool bold = simulations.HasFlag(StyleSimulations.BoldSimulation);

        content.Append(Invariant($"BT /{font.ResourceName} {Number(size)} Tf {ColorText(color)} rg "));
        // Render mode 2 fills the letters and also traces their outline, which thickens them (for fake bold).
        content.Append(bold ? Invariant($"{ColorText(color)} RG {Number(size * SimulatedBoldStroke)} w 2 Tr ") : "0 Tr ");

        var glyphs = run.GlyphIndices;
        var offsets = run.GlyphOffsets;
        bool simple = run.BidiLevel % 2 == 0 && (offsets == null || offsets.All(o => o == default));
        var origin = run.BaselineOrigin;

        // The text matrix puts a glyph's baseline at the origin. Its -1 turns the letters the right way up
        // again (the page is upside down, the WPF way), and the slant leans them for fake italics.
        string TextMatrix(double x, double y) => Invariant($"1 0 {Number(slant)} -1 {Number(x)} {Number(y)} Tm");

        if (simple)
        {
            content.Append(TextMatrix(origin.X, origin.Y)).Append(" [<");
            for (int i = 0; i < glyphs.Count; i++)
            {
                content.Append(glyphs[i].ToString("X4", CultureInfo.InvariantCulture));
                if (i == glyphs.Count - 1) break;
                // In 1000ths of the font size, subtracted from where the PDF would put the next glyph.
                double correction = (font.WidthOf(glyphs[i]) / 1000.0 * size - run.AdvanceWidths[i]) * 1000 / size;
                if (Math.Abs(correction) >= 0.05) content.Append("> ").Append(Number(correction)).Append(" <");
            }
            content.Append(">] TJ ET\n");
            return;
        }

        // Right-to-left text, or glyphs nudged from their usual spot (accents, say): place each glyph itself.
        double pen = 0;
        for (int i = 0; i < glyphs.Count; i++)
        {
            double advance = run.AdvanceWidths[i];
            var offset = offsets?[i] ?? default;
            double x = (run.BidiLevel % 2 == 1 ? origin.X - pen - advance : origin.X + pen) + offset.X;
            content.Append(TextMatrix(x, origin.Y - offset.Y)).Append(" <")
                .Append(glyphs[i].ToString("X4", CultureInfo.InvariantCulture)).Append("> Tj ");
            pen += advance;
        }
        content.Append("ET\n");
    }

    /// <summary>
    /// Tells the font which characters each glyph stands for. Usually that's one character per glyph, but a
    /// glyph can stand for several ("fi" drawn as one ligature), or several glyphs for one character.
    /// The run's cluster map says which characters go with which glyphs.
    /// </summary>
    private static void RememberCharacters(GlyphRun run, PdfFont font)
    {
        var glyphs = run.GlyphIndices;
        foreach (ushort glyph in glyphs) font.Use(glyph, null);

        var characters = run.Characters;
        var clusters = run.ClusterMap;
        if (characters == null || characters.Count == 0) return;
        if (clusters == null)
        {
            for (int i = 0; i < Math.Min(glyphs.Count, characters.Count); i++)
                font.Use(glyphs[i], characters[i].ToString());
            return;
        }

        for (int c = 0; c < characters.Count;)
        {
            int glyph = clusters[c];
            int end = c + 1;
            while (end < characters.Count && clusters[end] == glyph) end++;
            if (glyph < glyphs.Count)
                font.Use(glyphs[glyph], string.Concat(Enumerable.Range(c, end - c).Select(i => characters[i])));
            c = end;
        }
    }

    /// <summary>The packed-in font for a glyph typeface (fake bold and italic share their font with the plain one).</summary>
    private PdfFont? FontFor(GlyphTypeface typeface)
    {
        string key = $"{typeface.FontUri}|{string.Join(",", typeface.Win32FamilyNames.Values)}|{string.Join(",", typeface.Win32FaceNames.Values)}";
        if (!_fonts.TryGetValue(key, out var font))
        {
            font = PdfFont.TryCreate(typeface, $"F{_fonts.Count + 1}", _pdf.NewObject());
            _fonts[key] = font;
        }
        return font;
    }

    /// <summary>Lines and filled shapes, such as underlines and strikethroughs. Curves are approximated by short lines.</summary>
    private static void DrawShape(Geometry geometry, Brush? fill, Pen? pen, StringBuilder content)
    {
        bool filled = fill != null, stroked = pen is { Brush: not null, Thickness: > 0 };
        if (!filled && !stroked) return;

        var flat = geometry.GetFlattenedPathGeometry();
        var transform = flat.Transform?.Value ?? Matrix.Identity;
        foreach (var figure in flat.Figures)
        {
            content.Append(PointText(transform.Transform(figure.StartPoint))).Append(" m\n");
            foreach (var segment in figure.Segments)
            {
                var points = segment switch
                {
                    LineSegment line => [line.Point],
                    PolyLineSegment polyline => polyline.Points,
                    _ => (IList<Point>)[],
                };
                foreach (var point in points) content.Append(PointText(transform.Transform(point))).Append(" l\n");
            }
            if (figure.IsClosed) content.Append("h\n");
        }

        if (filled) content.Append(ColorText(ColorOf(fill))).Append(" rg ");
        if (stroked) content.Append(ColorText(ColorOf(pen!.Brush))).Append(Invariant($" RG {Number(pen.Thickness)} w "));
        bool evenOdd = flat.FillRule == FillRule.EvenOdd;
        content.Append((filled, stroked) switch
        {
            (true, true) => evenOdd ? "B*" : "B",
            (true, false) => evenOdd ? "f*" : "f",
            _ => "S",
        }).Append('\n');
    }

    // ── Writing numbers and colors ─────────────────────────────────────────

    private static Color ColorOf(Brush? brush) => brush is SolidColorBrush solid ? solid.Color : Colors.Black;

    private static string ColorText(Color color) => $"{Number(color.R / 255.0)} {Number(color.G / 255.0)} {Number(color.B / 255.0)}";

    private static string PointText(Point point) => $"{Number(point.X)} {Number(point.Y)}";

    private static string MatrixText(Matrix m) =>
        $"{Number(m.M11)} {Number(m.M12)} {Number(m.M21)} {Number(m.M22)} {Number(m.OffsetX)} {Number(m.OffsetY)}";

    /// <summary>PDF numbers: a dot for decimals whatever the computer's language, and never "1E-05".</summary>
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The PDF file being written: numbered objects one after another, then the "cross-reference table" that
/// says where in the file each object starts, so a viewer can jump straight to any page.
/// </summary>
internal sealed class PdfFile
{
    private readonly MemoryStream _output = new();
    private readonly Dictionary<int, long> _positions = [];
    private int _count;

    public PdfFile()
    {
        // The version, then a comment with a few non-ASCII bytes, which tells programs this file is binary.
        Write("%PDF-1.7\n");
        _output.Write([(byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n']);
    }

    /// <summary>Reserves the next object number, so other objects can refer to it before it's written.</summary>
    public int NewObject() => ++_count;

    public void WriteObject(int number, string dictionary)
    {
        _positions[number] = _output.Position;
        Write($"{number} 0 obj\n{dictionary}\nendobj\n");
    }

    /// <summary>An object holding data — page instructions, a font file — squeezed with the same compression as zip files.</summary>
    public void WriteStream(int number, string entries, byte[] data)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);

        _positions[number] = _output.Position;
        Write($"{number} 0 obj\n<< {entries} /Filter /FlateDecode /Length {compressed.Length} >>\nstream\n");
        compressed.Position = 0;
        compressed.CopyTo(_output);
        Write("\nendstream\nendobj\n");
    }

    /// <summary>Writes the cross-reference table and the trailer (which says where to start), and returns the file.</summary>
    public byte[] Finish(int root, int info)
    {
        long table = _output.Position;
        var xref = new StringBuilder();
        xref.Append($"xref\n0 {_count + 1}\n0000000000 65535 f\r\n");
        for (int number = 1; number <= _count; number++)
        {
            // Every entry is exactly 20 bytes. An object that was reserved but never written is marked free.
            xref.Append(_positions.TryGetValue(number, out long position)
                ? $"{position:D10} 00000 n\r\n"
                : "0000000000 65535 f\r\n");
        }
        xref.Append($"trailer\n<< /Size {_count + 1} /Root {root} 0 R /Info {info} 0 R >>\nstartxref\n{table}\n%%EOF\n");
        Write(xref.ToString());
        return _output.ToArray();
    }

    /// <summary>Text for things like the title: UTF-16 in hex, which works for any language.</summary>
    public static string TextString(string text) =>
        "<FEFF" + string.Concat(text.Select(c => ((int)c).ToString("X4", CultureInfo.InvariantCulture))) + ">";

    /// <summary>PDF dates look like (D:20261007153000+02'00').</summary>
    public static string Date(DateTimeOffset time)
    {
        var offset = time.Offset;
        string sign = offset < TimeSpan.Zero ? "-" : "+";
        return $"(D:{time.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)}{sign}{Math.Abs(offset.Hours):D2}'{Math.Abs(offset.Minutes):D2}')";
    }

    private void Write(string text) => _output.Write(Encoding.ASCII.GetBytes(text));
}
