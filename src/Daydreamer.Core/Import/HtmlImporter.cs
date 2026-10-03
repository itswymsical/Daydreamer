using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Daydreamer.Core.Models;

namespace Daydreamer.Core.Import;

/// <summary>
/// Reads the HTML that Google Docs, Word and web pages put on the clipboard when you copy,
/// keeping headings, lists, bold/italic and line spacing. It's a small, forgiving reader that
/// understands only the parts of HTML that matter here.
/// </summary>
public static partial class HtmlImporter
{
    // A comment, a Word "<![if ...]>" marker, or a tag (quoted attribute values may contain '>').
    [GeneratedRegex(@"<!--.*?-->|<!\[[^\]]*\]>|<(?<close>/)?(?<tag>[a-zA-Z][a-zA-Z0-9]*)(?<attrs>(?:""[^""]*""|'[^']*'|[^'"">])*)>", RegexOptions.Singleline)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"(?<name>[a-zA-Z_:][-a-zA-Z0-9_:.]*)\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'>]+))")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"line-height\s*:\s*(?<value>[\d.]+)\s*(?<unit>%|pt|px|em)?", RegexOptions.IgnoreCase)]
    private static partial Regex LineHeightRegex();

    [GeneratedRegex(@"mso-list\s*:[^;]*level(?<level>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WordListRegex();

    private enum Source { Web, GoogleDocs, Word }

    private sealed record Format(string Tag, bool Bold, bool Italic, bool Hidden, bool ListGlyph);

    private static readonly Format Plain = new("", false, false, false, false);

    private static readonly HashSet<string> BlockTags = ["p", "div", "h1", "h2", "h3", "h4", "h5", "h6", "li", "blockquote", "pre", "tr", "dt", "dd"];
    private static readonly HashSet<string> HiddenTags = ["style", "script", "head", "title", "xml", "template"];
    private static readonly HashSet<string> VoidTags = ["br", "img", "meta", "link", "hr", "input", "wbr", "col", "area", "base", "source"];

    public static ImportedDocument Read(string html)
    {
        string fragment = Fragment(html);
        var source = fragment.Contains("docs-internal-guid", StringComparison.Ordinal) ? Source.GoogleDocs
            : html.Contains("urn:schemas-microsoft-com:office", StringComparison.OrdinalIgnoreCase)
              || fragment.Contains("mso-", StringComparison.OrdinalIgnoreCase) ? Source.Word
            : Source.Web;

        var builder = new Builder();
        var formats = new List<Format>();
        var lists = new Stack<ListKind>();
        int position = 0;

        foreach (Match token in TokenRegex().Matches(fragment))
        {
            if (token.Index > position)
                builder.AddText(fragment[position..token.Index], Current(formats));
            position = token.Index + token.Length;
            if (!token.Groups["tag"].Success) continue; // a comment

            string tag = token.Groups["tag"].Value.ToLowerInvariant();
            if (token.Groups["close"].Success)
                Close(tag, formats, lists, builder);
            else
                Open(tag, Attributes(token.Groups["attrs"].Value), formats, lists, builder, source);
        }
        if (position < fragment.Length)
            builder.AddText(fragment[position..], Current(formats));

        builder.EndParagraph();
        return builder.Document;
    }

    private static void Open(string tag, Dictionary<string, string> attributes, List<Format> formats,
        Stack<ListKind> lists, Builder builder, Source source)
    {
        if (VoidTags.Contains(tag))
        {
            if (tag == "br") builder.LineBreak();
            return;
        }

        string style = attributes.GetValueOrDefault("style", "");
        var parent = Current(formats);

        if (tag is "ul" or "ol")
            lists.Push(tag == "ul" ? ListKind.Bullet : ListKind.Numbered);

        if (BlockTags.Contains(tag))
        {
            // Google Docs wraps each list item's text in <p>; that's still the same item.
            bool continuesListItem = tag == "p" && builder.IsInListItem;
            if (!continuesListItem)
                StartBlock(tag, attributes, style, lists, builder, source);
        }

        bool bold = parent.Bold || tag is "b" or "strong" || tag is "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
        bool italic = parent.Italic || tag is "i" or "em";
        bold = StyleFlag(style, "font-weight", bold, v => v is "bold" or "bolder" || v.Length == 3 && v[0] >= '6' && char.IsDigit(v[0]),
            v => v is "normal" or "lighter" || v.Length == 3 && v[0] <= '5' && char.IsDigit(v[0]));
        italic = StyleFlag(style, "font-style", italic, v => v is "italic" or "oblique", v => v == "normal");

        bool listGlyph = style.Contains("mso-list:ignore", StringComparison.OrdinalIgnoreCase)
                         || style.Contains("mso-list: ignore", StringComparison.OrdinalIgnoreCase);
        bool hidden = parent.Hidden || HiddenTags.Contains(tag) || listGlyph;
        formats.Add(new Format(tag, bold, italic, hidden, listGlyph || parent.ListGlyph));
    }

    private static void StartBlock(string tag, Dictionary<string, string> attributes, string style,
        Stack<ListKind> lists, Builder builder, Source source)
    {
        int? heading = tag.Length == 2 && tag[0] == 'h' && char.IsDigit(tag[1]) ? tag[1] - '0' : null;
        string cssClass = attributes.GetValueOrDefault("class", "");
        if (cssClass.Contains("title", StringComparison.OrdinalIgnoreCase) && !cssClass.Contains("subtitle", StringComparison.OrdinalIgnoreCase))
            heading = 0;

        var list = ListKind.None;
        int level = 0;
        bool isChecked = false;
        if (tag == "li")
        {
            list = lists.Count > 0 ? lists.Peek() : ListKind.Bullet;
            level = Math.Max(0, lists.Count - 1);
            if (attributes.TryGetValue("aria-level", out var ariaLevel) && int.TryParse(ariaLevel, out int aria))
                level = Math.Max(0, aria - 1);
            if (attributes.GetValueOrDefault("role") == "checkbox")
            {
                list = ListKind.Checkbox;
                isChecked = attributes.GetValueOrDefault("aria-checked") == "true";
            }
        }
        else if (WordListRegex().Match(style) is { Success: true } wordList)
        {
            list = ListKind.Bullet; // confirmed or changed to numbered when its hidden "1." glyph is read
            level = Math.Max(0, int.Parse(wordList.Groups["level"].Value, CultureInfo.InvariantCulture) - 1);
        }

        builder.StartParagraph(new ImportedParagraph("", heading, list, level, isChecked, LineSpacing(style, source)), fromListTag: tag == "li");
    }

    private static void Close(string tag, List<Format> formats, Stack<ListKind> lists, Builder builder)
    {
        int index = formats.FindLastIndex(f => f.Tag == tag);
        if (index >= 0) formats.RemoveRange(index, formats.Count - index);

        if (tag is "ul" or "ol" && lists.Count > 0) lists.Pop();
        if (BlockTags.Contains(tag) && !(tag == "p" && builder.IsInListItem)) builder.EndParagraph();
        if (tag == "li") builder.EndParagraph();
    }

    /// <summary>Google Docs writes 1.15 spacing as line-height:1.38 (it multiplies by 1.2); Word writes 1.5 lines as 150%.</summary>
    private static double? LineSpacing(string style, Source source)
    {
        var match = LineHeightRegex().Match(style);
        if (!match.Success || !double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return null;

        double? spacing = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "" or "em" => value / 1.2,
            "%" => source == Source.Word ? value / 100 : value / 100 / 1.2,
            _ => null, // points/pixels depend on the font size; not worth guessing
        };
        return spacing is >= 0.5 and <= 4 ? Math.Round(spacing.Value, 2) : null;
    }

    private static bool StyleFlag(string style, string property, bool inherited, Func<string, bool> isOn, Func<string, bool> isOff)
    {
        foreach (string declaration in style.Split(';'))
        {
            int colon = declaration.IndexOf(':');
            if (colon < 0 || !declaration[..colon].Trim().Equals(property, StringComparison.OrdinalIgnoreCase)) continue;
            string value = declaration[(colon + 1)..].Trim().ToLowerInvariant();
            if (isOn(value)) return true;
            if (isOff(value)) return false;
        }
        return inherited;
    }

    private static Dictionary<string, string> Attributes(string text)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in AttributeRegex().Matches(text))
            attributes[m.Groups["name"].Value] = WebUtility.HtmlDecode(m.Groups["value"].Value);
        return attributes;
    }

    private static Format Current(List<Format> formats) => formats.Count > 0 ? formats[^1] : Plain;

    /// <summary>Clipboard HTML wraps the copied part in StartFragment/EndFragment comments.</summary>
    private static string Fragment(string html)
    {
        int start = html.IndexOf("<!--StartFragment-->", StringComparison.OrdinalIgnoreCase);
        int end = html.IndexOf("<!--EndFragment-->", StringComparison.OrdinalIgnoreCase);
        if (start >= 0 && end > start) return html[(start + "<!--StartFragment-->".Length)..end];

        int body = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (body >= 0)
        {
            int open = html.IndexOf('>', body);
            if (open >= 0) return html[(open + 1)..];
        }
        return html;
    }

    /// <summary>Collects text into paragraphs as the tags open and close.</summary>
    private sealed class Builder
    {
        private readonly List<TextRun> _runs = [];
        private ImportedParagraph? _paragraph;
        private bool _fromListTag;
        private string _listGlyph = "";

        public ImportedDocument Document { get; } = new();

        /// <summary>Inside an &lt;li&gt; (Word's list paragraphs are plain &lt;p&gt; tags, so they don't count).</summary>
        public bool IsInListItem => _paragraph != null && _fromListTag;

        public void StartParagraph(ImportedParagraph paragraph, bool fromListTag = false)
        {
            EndParagraph();
            _paragraph = paragraph;
            _fromListTag = fromListTag;
        }

        public void AddText(string raw, Format format)
        {
            string text = WebUtility.HtmlDecode(raw);
            if (format.Hidden)
            {
                if (format.ListGlyph) _listGlyph += text;
                return;
            }
            if (_paragraph == null)
            {
                if (string.IsNullOrWhiteSpace(text)) return; // spacing between tags, not content
                _paragraph = new ImportedParagraph("");
            }
            _runs.Add(new TextRun(text, format.Bold, format.Italic));
        }

        /// <summary>A &lt;br&gt;: ends the current line, or between paragraphs, makes an empty one.</summary>
        public void LineBreak()
        {
            if (_paragraph == null)
                Document.Paragraphs.Add(new ImportedParagraph(""));
            else
            {
                var carry = _paragraph with { Heading = null, List = ListKind.None, Text = "" };
                EndParagraph();
                _paragraph = carry;
            }
        }

        public void EndParagraph()
        {
            if (_paragraph == null) return;
            var paragraph = _paragraph;
            string text = RunText.Compose(_runs, plain: paragraph.Heading != null, collapseWhitespace: true);

            // Word lists: the hidden glyph says whether it's "1." (numbered) or "·" (bulleted).
            if (paragraph.List == ListKind.Bullet && _listGlyph.Trim().TrimEnd('.', ')').Any(char.IsLetterOrDigit))
                paragraph = paragraph with { List = ListKind.Numbered };

            if (text.Length > 0 || paragraph is { Heading: null })
                Document.Paragraphs.Add(paragraph with { Text = text });

            _runs.Clear();
            _listGlyph = "";
            _paragraph = null;
            _fromListTag = false;
        }
    }
}
