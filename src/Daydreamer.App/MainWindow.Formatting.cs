using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Daydreamer.App.Controls;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Theming;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;

namespace Daydreamer.App;

// The toolbar's font / size / color controls, and keeping the editor in step with light and dark mode.
// (MainWindow is split across files by topic; this is the same class as MainWindow.xaml.cs.)
public partial class MainWindow
{
    /// <summary>Points the toolbar's formatting controls at the kind of line under the cursor.</summary>
    private void UpdateFormatTarget()
    {
        if (_project == null || _document == null || _styleEntries.Count == 0)
        {
            _formatTarget.Clear();
            return;
        }

        var sheet = _project.Styles;
        var line = Editor.Document.GetLineByOffset(Editor.CaretOffset);
        var parsed = _parser.ParseLine(Editor.Document.GetText(line));

        if (parsed.Character is { } character)
        {
            _formatTarget.Set(character.Name, "", character.Style,
                palette => StyleResolver.ResolveCharacter(sheet, character, palette), character);
            return;
        }

        // An empty line edits "Default text"; anything else edits its kind of line (SFX, Caption, ...).
        var entry = parsed.Type == ElementType.Blank
            ? _styleEntries[0]
            : _styleEntries.FirstOrDefault(s => s.Type == parsed.Type) ?? _styleEntries[0];
        _formatTarget.Set(entry.Name, "", entry.Style, entry.Resolve, entry);
    }

    /// <summary>The pill at the start of the font controls opens that style's full settings.</summary>
    private void FormatChip_Click(object sender, RoutedEventArgs e)
    {
        switch (_formatTarget.Source)
        {
            case Character character:
                SelectCharacter(character);
                break;
            case StyleEntry entry:
                FormattingTabs.SelectedIndex = 1;
                StylesList.SelectedItem = entry;
                StylesList.ScrollIntoView(entry);
                break;
        }
    }

    /// <summary>The line spacing menu: Single, 1.15, 1.5, Double, or a number of your own.</summary>
    private void LineSpacingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_formatTarget.LineSpacing is not { } current) return;

        var menu = new ContextMenu { PlacementTarget = LineSpacingButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Line spacing  ·  {_formatTarget.Label}", IsEnabled = false });

        foreach (double spacing in FontList.LineSpacings)
        {
            var item = new MenuItem
            {
                Header = spacing switch { 1 => "Single", 2 => "Double", _ => spacing.ToString("0.##", CultureInfo.CurrentCulture) },
                IsCheckable = true,
                IsChecked = Math.Abs(current - spacing) < 0.001,
            };
            item.Click += (_, _) => ApplyLineSpacing(spacing);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var custom = new MenuItem { Header = "Custom…" };
        custom.Click += (_, _) => AskForLineSpacing(current);
        menu.Items.Add(custom);

        menu.IsOpen = true;
    }

    private void AskForLineSpacing(double current)
    {
        string? text = PromptDialog.Ask(this, "Line spacing",
            $"Line spacing for every {_formatTarget.Label} line (1 = single, 2 = double):",
            current.ToString("0.##", CultureInfo.CurrentCulture), "Apply");
        if (text == null) return;

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double spacing) && spacing is >= 0.5 and <= 4)
            ApplyLineSpacing(spacing);
        else
            PromptDialog.Inform(this, "Line spacing", "Line spacing should be a number between 0.5 and 4 — for example 1.3.");
    }

    private void ApplyLineSpacing(double spacing)
    {
        _formatTarget.LineSpacing = spacing;
        Editor.Focus();
    }

    private void ColorPicker_Picked(object? sender, EventArgs e)
    {
        ColorPopup.IsOpen = false;
        Editor.Focus();
    }

    // After picking from a toolbar list, go straight back to writing.
    private void ToolbarCombo_DropDownClosed(object? sender, EventArgs e) => Editor.Focus();

    private void ToolbarSize_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        ToolbarSize.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource(); // apply now, not after the typing pause
        Editor.Focus();
        e.Handled = true;
    }

    /// <summary>Colors the editor draws itself: caret, selection, search highlights, and the page shadow.</summary>
    private void ApplyEditorTheme()
    {
        _colorizer.Palette = ThemeManager.Palette;
        Editor.TextArea.Caret.CaretBrush = (Brush)FindResource("TextBrush");
        Editor.TextArea.SelectionBrush = (Brush)FindResource("EditorSelectionBrush");
        if (_searchPanel != null) _searchPanel.MarkerBrush = (Brush)FindResource("SearchMarkerBrush");
        PaperShadow.Opacity = (double)FindResource("PageShadowOpacity");
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        ApplyEditorTheme();
        ApplyFormatting();
        CharacterList.Items.Refresh(); // re-run the name-color converters against the new page color
        if (IsHomeShown) RecentList.ItemsSource = BuildHomeItems(); // redraw the page previews in the new colors
        UpdateAmbient();
        UpdateShellButtons();
    }
}
