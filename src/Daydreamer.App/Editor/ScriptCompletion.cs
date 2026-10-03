using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;

namespace Daydreamer.App.Editor;

/// <summary>One entry in the pop-up that appears when you start typing a name in CAPS (or press Ctrl+Space).</summary>
public sealed class ScriptCompletionData : ICompletionData
{
    private readonly string _insertText;

    public ScriptCompletionData(string text, string insertText, string description, object? content = null)
    {
        Text = text;
        _insertText = insertText;
        Description = description;
        Content = content ?? text;
    }

    public System.Windows.Media.ImageSource? Image => null;

    /// <summary>What the pop-up filters on as you type.</summary>
    public string Text { get; }

    public object Content { get; }
    public object Description { get; }
    public double Priority => 0;

    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs) =>
        textArea.Document.Replace(completionSegment, _insertText);

    /// <summary>A character entry: their name drawn in their own font (and name color, if they have one).</summary>
    /// <param name="separator">": " for scripts that write "AKIRA: Hi", " " for ones that write "AKIRA Hi".</param>
    public static ScriptCompletionData ForCharacter(Character character, ThemePalette palette, string separator = ": ")
    {
        var label = new TextBlock { Text = character.Name, FontWeight = FontWeights.SemiBold };
        if (character.NameColor != null)
            label.Foreground = WpfStyles.ReadableBrush(character.NameColor, palette.Paper);
        if (character.Style.FontFamily != null)
            label.FontFamily = WpfStyles.Family(character.Style.FontFamily, 1);

        // Without a colon, a name is only recognised in capitals.
        string name = separator == " " ? character.Name.ToUpperInvariant() : character.Name;
        string description = string.IsNullOrWhiteSpace(character.Notes) ? "Character" : character.Notes;
        return new ScriptCompletionData(character.Name, name + separator, description, label);
    }
}
