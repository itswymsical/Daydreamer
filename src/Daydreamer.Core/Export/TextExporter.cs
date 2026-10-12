using System.Text;

namespace Daydreamer.Core.Export;

/// <summary>
/// Plain text (.txt): the words exactly as the page shows them — labels like "AKIRA:" and "SFX:" included,
/// Daydreamer's hidden {tags} and the ** around bold left out. Indents are tabs, list items start with
/// their bullet, number or checkbox.
/// </summary>
public static class TextExporter
{
    public static string ToText(ExportDocument document)
    {
        var text = new StringBuilder();
        foreach (var paragraph in document.Paragraphs)
        {
            text.Append('\t', paragraph.IndentLevel);
            if (paragraph.Marker.Length > 0) text.Append(paragraph.Marker).Append(' ');
            // Windows line endings, so even old programs show the lines apart.
            text.Append(paragraph.Text).Append("\r\n");
        }
        return text.ToString();
    }

    /// <summary>
    /// The file's bytes: UTF-8 with a byte order mark (like Google Docs' .txt download), so Notepad and
    /// Word read accented and Japanese characters correctly.
    /// </summary>
    public static byte[] Export(ExportDocument document) =>
        [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(ToText(document))];
}
