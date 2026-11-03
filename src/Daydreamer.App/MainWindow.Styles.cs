using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Daydreamer.App.Dialogs;
using Daydreamer.Core.Editing;
using Daydreamer.Core.Formatting;
using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;

namespace Daydreamer.App;

// Styles you make yourself: for one document, or for every document in a project. A line wears one in its hidden
// tag ({style:Letter}), picked from the line menu, and it goes on top of whatever else the line is.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private List<StyleEntry> _customEntries = [];

    /// <summary>Tells the parser which of your styles this document can use, and lists them on the Styles tab.</summary>
    private void RefreshCustomStyles(NamedStyle? select = null)
    {
        select ??= (CustomStylesList.SelectedItem as StyleEntry)?.Custom;
        _parser.CustomStyles = _project?.StylesFor(_document?.Title) ?? [];

        _customEntries = [];
        if (_project != null)
        {
            var project = _project;
            var sheet = project.Styles;
            StyleEntry Entry(NamedStyle style, bool onlyThisDocument) => new(style.Style, type: null,
                palette => StyleResolver.Merge(StyleResolver.Start(palette.Text), sheet.Base, style.Style), project.Keywords, style, onlyThisDocument);

            _customEntries.AddRange(project.DocumentStyles(_document?.Title).Select(s => Entry(s, onlyThisDocument: true)));
            _customEntries.AddRange(project.CustomStyles.Select(s => Entry(s, onlyThisDocument: false)));
        }
        CustomStylesList.ItemsSource = _customEntries;
        CustomStylesList.SelectedItem = _customEntries.FirstOrDefault(e => e.Custom == select);
    }

    private void AddStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null) return;
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        foreach (var item in NewStyleItems(applyToSelection: false)) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    /// <summary>The two ways to start a new style: just for this document, or for the whole project.</summary>
    private List<MenuItem> NewStyleItems(bool applyToSelection)
    {
        MenuItem Item(string header, bool onlyThisDocument)
        {
            var item = new MenuItem { Header = header, IsEnabled = !onlyThisDocument || _document != null };
            item.Click += (_, _) =>
            {
                if (CreateStyle(onlyThisDocument) is { } style && applyToSelection)
                    ApplyCustomStyle(style.Name);
            };
            return item;
        }
        return [Item("New style for this document…", true), Item("New style for the whole project…", false)];
    }

    private NamedStyle? CreateStyle(bool onlyThisDocument)
    {
        if (_project == null) return null;
        string? name = PromptDialog.Ask(this, "New style", "What's it for? (a letter, an inscription, a flashback…)", "", "Create");
        name = NamedStyle.CleanName(name);
        if (name.Length == 0) return null;

        var existing = _project.StylesFor(_document?.Title).FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            SelectCustomStyle(existing); // one of that name is already here: show it instead
            return existing;
        }

        var style = new NamedStyle { Name = name };
        if (onlyThisDocument && _document != null) _project.SettingsFor(_document.Title).Styles.Add(style);
        else _project.CustomStyles.Add(style);
        RefreshCustomStyles(style);
        SelectCustomStyle(style);
        return style;
    }

    private void SelectCustomStyle(NamedStyle style)
    {
        ShowSidePanelTab(StylesTab);
        CustomStylesList.SelectedItem = _customEntries.FirstOrDefault(e => e.Custom == style);
        if (CustomStylesList.SelectedItem != null) CustomStylesList.ScrollIntoView(CustomStylesList.SelectedItem);
    }

    /// <summary>Puts the selected lines in a style (or, with null, takes them out of theirs).</summary>
    private void ApplyCustomStyle(string? name)
    {
        if (_document == null) return;
        var (first, last) = SelectedLines();
        bool oneLine = first.LineNumber == last.LineNumber;
        var lines = LinesBetween(first, last).Select(text =>
            !oneLine && string.IsNullOrWhiteSpace(text) ? text : LineEditing.SetLineStyle(text, name, _parser)).ToList();
        ReplaceLines(first, last, lines);
        UpdateCaretInfo();
    }

    private void RenameCustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || ((FrameworkElement)sender).DataContext is not StyleEntry { Custom: { } style } entry) return;
        string? typed = PromptDialog.Ask(this, "Rename style", "New name:", style.Name, "Rename");
        string name = NamedStyle.CleanName(typed);
        if (name.Length == 0 || name == style.Name) return;
        if (_project.StylesFor(_document?.Title).Any(s => s != style && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            PromptDialog.Inform(this, "Rename style", $"There's already a style called {name}.");
            return;
        }

        SaveDocument();
        string oldName = style.Name;
        if (!entry.OnlyThisDocument) RenameStyleInOtherDocuments(oldName, name);
        RenameStyleInOpenDocument(oldName, name);
        style.Name = name;
        RefreshCustomStyles(style);
    }

    /// <summary>The lines in a style say its name in their tags, so a project style's new name goes through every document.</summary>
    private void RenameStyleInOtherDocuments(string oldName, string newName)
    {
        foreach (var document in _library.ListDocuments(_project!))
        {
            // A document with its own style of that name keeps it; those lines were never using the project's.
            if (document.FilePath == _document?.FilePath || _project!.DocumentStyles(document.Title).Any(s => s.Name.Equals(oldName, StringComparison.OrdinalIgnoreCase)))
                continue;
            try
            {
                string text = _library.ReadDocument(document);
                string renamed = LineEditing.RenameStyle(text, oldName, newName, _parser);
                if (renamed != text) _library.WriteDocument(document, renamed);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                PromptDialog.Inform(this, "Rename style", $"Couldn't update \"{document.Title}\", so its lines lost the style. Choose it again from the line menu.\n\n{ex.Message}");
            }
        }
    }

    private void RenameStyleInOpenDocument(string oldName, string newName)
    {
        if (_document == null) return;
        var document = Editor.Document;
        using (document.RunUpdate())
        {
            foreach (var line in document.Lines.ToList())
            {
                string text = document.GetText(line);
                if (string.Equals(_parser.SplitTag(text).Style, oldName, StringComparison.OrdinalIgnoreCase))
                    ReplaceChangedPart(line, LineEditing.SetLineStyle(text, newName, _parser));
            }
        }
    }

    /// <summary>Moves a style between "just this document" and "the whole project".</summary>
    private void MoveCustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || _document == null || ((FrameworkElement)sender).DataContext is not StyleEntry { Custom: { } style } entry) return;
        var target = entry.OnlyThisDocument ? _project.CustomStyles : _project.SettingsFor(_document.Title).Styles;
        if (target.Any(s => s != style && s.Name.Equals(style.Name, StringComparison.OrdinalIgnoreCase)))
        {
            PromptDialog.Inform(this, "Styles", $"There's already a style called {style.Name} there. Rename one of them first.");
            _project.DropIfEmpty(_document.Title);
            return;
        }

        if (entry.OnlyThisDocument) _project.SettingsFor(_document.Title).Styles.Remove(style);
        else _project.CustomStyles.Remove(style);
        target.Add(style);
        _project.DropIfEmpty(_document.Title);
        RefreshCustomStyles(style);
    }

    private void DeleteCustomStyle_Click(object sender, RoutedEventArgs e)
    {
        if (_project == null || ((FrameworkElement)sender).DataContext is not StyleEntry { Custom: { } style } entry) return;
        bool confirmed = PromptDialog.Confirm(this, "Remove style",
            $"Remove the {style.Name} style?\n\nLines in it keep their words and go back to their usual look.", "Remove");
        if (!confirmed) return;

        if (entry.OnlyThisDocument && _document != null)
        {
            _project.SettingsFor(_document.Title).Styles.Remove(style);
            _project.DropIfEmpty(_document.Title);
        }
        else
        {
            _project.CustomStyles.Remove(style);
        }
        RefreshCustomStyles();
    }
}
