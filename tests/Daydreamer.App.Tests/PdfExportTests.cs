using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Daydreamer.App.Export;
using Daydreamer.Core.Export;
using Daydreamer.Core.Models;

namespace Daydreamer.App.Tests;

public partial class PdfExportTests
{
    private static byte[] Pdf(params string[] lines)
    {
        var project = new Project();
        project.Characters.Add(new Character { Name = "AKIRA" });
        var document = ExportDocument.Build("Test ✓", string.Join("\n", lines), project);
        return PdfWriter.Export(document, PaperSize.Letter);
    }

    /// <summary>The PDF as text. Its structure is ASCII; Latin-1 keeps every byte of the binary parts as one character.</summary>
    private static string Text(byte[] pdf) => Encoding.Latin1.GetString(pdf);

    [GeneratedRegex(@"(\d+) 0 obj\n<< ([^\n]*?)/Filter /FlateDecode /Length (\d+) >>\nstream\n")]
    private static partial Regex StreamRegex();

    /// <summary>Every compressed stream in the file, unpacked: page instructions, font files, character tables.</summary>
    private static List<string> Streams(byte[] pdf)
    {
        var streams = new List<string>();
        foreach (Match match in StreamRegex().Matches(Text(pdf)))
        {
            int start = match.Index + match.Length, length = int.Parse(match.Groups[3].Value);
            using var zlib = new ZLibStream(new MemoryStream(pdf, start, length), CompressionMode.Decompress);
            using var unpacked = new MemoryStream();
            zlib.CopyTo(unpacked);
            streams.Add(Encoding.Latin1.GetString(unpacked.ToArray()));
        }
        return streams;
    }

    [Fact]
    public void Every_object_is_where_the_cross_reference_table_says()
    {
        var pdf = Pdf("PAGE 1", "AKIRA: Hello.");
        string text = Text(pdf);

        Assert.StartsWith("%PDF-1.7\n", text);
        Assert.EndsWith("%%EOF\n", text);
        int table = int.Parse(Regex.Match(text, @"startxref\n(\d+)\n%%EOF\n$").Groups[1].Value);
        Assert.StartsWith("xref\n", text[table..]);

        var entries = Regex.Matches(text[table..], @"(\d{10}) 00000 n\r\n");
        Assert.NotEmpty(entries);
        int number = 1;
        foreach (Match entry in entries)
        {
            int position = int.Parse(entry.Groups[1].Value);
            Assert.StartsWith($"{number++} 0 obj\n", text[position..]);
        }
    }

    [Fact]
    public void Long_documents_go_onto_several_pages()
    {
        var lines = Enumerable.Range(1, 150).Select(i => $"AKIRA: Line number {i}.").ToArray();
        string text = Text(Pdf(lines));

        int count = int.Parse(Regex.Match(text, @"/Type /Pages /Kids \[[^\]]*\] /Count (\d+)").Groups[1].Value);
        Assert.True(count >= 3, $"{count} pages");
        Assert.Equal(count, Regex.Matches(text, "/Type /Page ").Count);
    }

    [Fact]
    public void An_empty_document_is_one_empty_page()
    {
        Assert.Contains("/Count 1 ", Text(Pdf("")));
    }

    [Fact]
    public void Fonts_are_packed_in_and_the_text_reads_back_as_the_real_words()
    {
        var pdf = Pdf("AKIRA: Hello, world.");
        string text = Text(pdf);
        Assert.Contains("/FontFile2", text);
        Assert.Contains("/Title <FEFF", text); // the title, in a form that holds any language

        // Read the text the way a PDF viewer does when you copy it: glyph numbers from the page's TJ lists,
        // turned into characters with the fonts' glyph → character tables.
        var streams = Streams(pdf);
        var characters = new Dictionary<string, string>();
        foreach (string map in streams.Where(s => s.Contains("beginbfchar")))
        {
            foreach (Match entry in Regex.Matches(map, @"<([0-9A-F]{4})> <([0-9A-F]+)>"))
            {
                string hex = entry.Groups[2].Value;
                characters.TryAdd(entry.Groups[1].Value, string.Concat(Enumerable.Range(0, hex.Length / 4)
                    .Select(i => (char)Convert.ToInt32(hex.Substring(i * 4, 4), 16))));
            }
        }
        string page = streams.Single(s => s.StartsWith("q 0.75 0 0 -0.75")); // a page's instructions
        string read = string.Concat(Regex.Matches(page, @"<([0-9A-F]+)>").SelectMany(m => m.Groups[1].Value.Chunk(4))
            .Select(glyph => characters.GetValueOrDefault(new string(glyph), "?")));

        Assert.Equal("AKIRA: Hello, world.", read);
    }
}
