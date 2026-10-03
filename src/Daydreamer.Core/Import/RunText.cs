using System.Text;
using System.Text.RegularExpressions;

namespace Daydreamer.Core.Import;

/// <summary>A stretch of text with one look, as Word and HTML store it.</summary>
public readonly record struct TextRun(string Text, bool Bold, bool Italic);

/// <summary>Joins formatted runs into one line, writing bold as **bold** and italic as *italic*.</summary>
public static partial class RunText
{
    [GeneratedRegex(@"[ \t\r\n\f]+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex DoubleSpaceRegex();

    /// <param name="plain">Drop the bold/italic (used for headings, which have their own style).</param>
    /// <param name="collapseWhitespace">HTML treats runs of spaces and newlines as one space; Word doesn't.</param>
    public static string Compose(IEnumerable<TextRun> runs, bool plain, bool collapseWhitespace)
    {
        var merged = new List<TextRun>();
        foreach (var run in runs)
        {
            string text = collapseWhitespace ? WhitespaceRegex().Replace(run.Text, " ") : run.Text;
            if (text.Length == 0) continue;
            bool bold = run.Bold && !plain, italic = run.Italic && !plain && !bold; // bold wins over bold+italic
            if (merged.Count > 0 && merged[^1].Bold == bold && merged[^1].Italic == italic)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + text };
            else
                merged.Add(new TextRun(text, bold, italic));
        }

        var line = new StringBuilder();
        foreach (var run in merged)
        {
            if (!run.Bold && !run.Italic)
            {
                line.Append(run.Text);
                continue;
            }

            // Markers must hug the words ("**word** " not "**word **"), so move spaces outside them.
            string core = run.Text.Trim();
            if (core.Length == 0)
            {
                line.Append(run.Text);
                continue;
            }
            string marker = run.Bold ? "**" : "*";
            int lead = run.Text.Length - run.Text.TrimStart().Length;
            int trail = run.Text.Length - run.Text.TrimEnd().Length;
            line.Append(run.Text, 0, lead).Append(marker).Append(core).Append(marker).Append(run.Text, run.Text.Length - trail, trail);
        }

        string result = line.ToString();
        if (collapseWhitespace) result = DoubleSpaceRegex().Replace(result, " ");
        return result.Replace(' ', ' ').Trim();
    }
}
