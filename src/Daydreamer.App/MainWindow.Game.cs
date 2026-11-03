using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Daydreamer.App.Dialogs;
using Daydreamer.Core.Game;
using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;
using Microsoft.Win32;

namespace Daydreamer.App;

// Game dialogue: one document per character, written like a script, sent to the game as its localization (HJSON)
// and its definition (C#). The work is done in Core/Game; this is the buttons and the questions.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    /// <summary>The effects in the editor's right-click menu: what the menu says, what goes in the text.</summary>
    private static readonly (string Header, string Word)[] EffectMenu =
    [
        ("Shake", "shake"), ("Wave", "wave"), ("Type slower", "slow"), ("Type faster", "fast"),
        ("Voice rises", "up"), ("Voice falls", "down"), ("Ask it (late rise)", "question"), ("Stress it", "emphasis"), ("Voice wavers", "waver"),
    ];

    // ── The game folder ────────────────────────────────────────────────────

    private void ChooseGameFolder_Click(object sender, RoutedEventArgs e) => ChooseGameFolder();

    /// <summary>The mod this project's dialogue goes to, asking if it isn't known yet. Null if cancelled.</summary>
    private GameFolder? EnsureGameFolder()
    {
        if (_project?.GameFolder is { } known && GameFolder.Locate(known) is { } folder) return folder;
        return ChooseGameFolder();
    }

    private GameFolder? ChooseGameFolder()
    {
        if (_project == null) return null;
        var dialog = new OpenFolderDialog { Title = "Choose the mod's folder (the one with Localization in it)" };
        if (_project.GameFolder is { } current && Directory.Exists(current)) dialog.InitialDirectory = current;
        if (dialog.ShowDialog(this) != true) return null;

        var folder = GameFolder.Locate(dialog.FolderName);
        if (folder == null)
        {
            PromptDialog.Inform(this, "Game folder", "There's no Localization folder in there. Choose the mod's own folder, the one that has Localization in it.");
            return null;
        }
        UseGameFolder(folder);
        return folder;
    }

    private void UseGameFolder(GameFolder folder)
    {
        if (_project == null) return;
        _project.GameFolder = folder.Root;
        SaveProject();
        ApplyFormatting(); // dialogue markup is on for projects with a game folder
        UpdateGameFolderLabel();
    }

    private void UpdateGameFolderLabel()
    {
        GameFolderLabel.Text = _project?.GameFolder ?? "No game folder yet. You'll be asked for it the first time you send a document.";
        GameFolderLabel.ToolTip = _project?.GameFolder;
    }

    // ── Sending a document to the game ─────────────────────────────────────

    private MenuItem GameExportItem(DocumentInfo document)
    {
        var item = new MenuItem
        {
            Header = "Send to the game (HJSON + C#)",
            ToolTip = "One document per character: writes their words into the mod's localization and their conversations into a definition file.",
        };
        item.Click += (_, _) => SendToGame(document);
        return item;
    }

    private void SendToGame(DocumentInfo document)
    {
        if (_project == null || EnsureGameFolder() is not { } folder) return;

        bool isOpen = document.FilePath == _document?.FilePath;
        if (isOpen) SaveDocument();
        string text = isOpen ? Editor.Text : _library.ReadDocument(document);

        DialogueBook book;
        GameExportPlan plan;
        try
        {
            book = DialogueBook.Parse(document.Title, text, _project);
            if (book.Conversations.Count == 0)
            {
                PromptDialog.Inform(this, "Send to the game", "There's nothing to send yet. Start a conversation with a # heading, and write its lines under it. The Guide tab shows how.");
                return;
            }
            plan = GameExporter.Plan(book, folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HjsonException)
        {
            PromptDialog.Inform(this, "Send to the game", $"Couldn't read the game's files, so nothing was changed.\n\n{ex.Message}");
            return;
        }

        var problems = ProblemsIn(book, plan);
        if (problems.Count > 0 && !PromptDialog.Confirm(this, "Send to the game",
                $"A few things won't work the way they read:\n\n{string.Join("\n", problems)}", "Send anyway"))
            return;

        // The document is the character's whole script, but it shouldn't take anything away without asking.
        if (plan.NotInDocument.Count > 0 && PromptDialog.Confirm(this, "Send to the game",
                $"The game has {Plural(plan.NotInDocument.Count, "conversation")} for {book.Group} that this document doesn't:\n\n{Listed(plan.NotInDocument)}\n\nKeep them in the game?",
                "Remove them", cancelText: "Keep them"))
        {
            plan = GameExporter.Plan(book, folder, removeOthers: true);
        }

        if (!plan.HasChanges)
        {
            ShowInfo($"The game already has this version of {book.Group}.", "OK", () => { });
            return;
        }

        try
        {
            GameExporter.Apply(plan, Path.Combine(_project.FolderPath, Library.TrashFolderName, "Game exports"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PromptDialog.Inform(this, "Send to the game", $"Couldn't save to the game's folder. If a file is open in another program, close it there and try again.\n\n{ex.Message}");
            return;
        }

        var sent = new List<string>();
        if (plan.Added.Count > 0) sent.Add($"{plan.Added.Count} new");
        if (plan.Updated.Count > 0) sent.Add($"{plan.Updated.Count} changed");
        string what = sent.Count > 0 ? $"{string.Join(", ", sent)} of {Plural(book.Conversations.Count, "conversation")}" : Plural(book.Conversations.Count, "conversation");
        string first = plan.Changes.FirstOrDefault()?.Path ?? folder.Root;
        ShowInfo($"Sent {book.Group} to the game: {what}. Reload the mod to hear it.", "Show files", () => RevealFile(first), TimeSpan.FromSeconds(30));
    }

    private static List<string> ProblemsIn(DialogueBook book, GameExportPlan plan)
    {
        var problems = book.Issues.Select(issue => $"Line {issue.Line}: {issue.Message}").ToList();
        problems.AddRange(plan.BrokenBranches.Select(branch => $"A choice goes nowhere: {branch} isn't written yet."));
        problems.AddRange(plan.MismatchedAnchors);
        problems.AddRange(plan.MissingProfiles.Select(speaker =>
            $"{speaker} has no speaker profile in the mod, so they'll speak in gray with no portrait or voice. Add \"// color: 255 110 80\" to this document for a color, or a profile to SpeakerRegistry."));
        return problems;
    }

    /// <summary>A short list for a dialog: the first few, and how many more.</summary>
    private static string Listed(List<string> items)
    {
        const int shown = 8;
        string list = string.Join("\n", items.Take(shown));
        return items.Count > shown ? $"{list}\n…and {items.Count - shown} more" : list;
    }

    private static void RevealFile(string path) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });

    // ── Bringing existing dialogue in ──────────────────────────────────────

    /// <summary>
    /// Importing a localization file makes one document per character in it, joined up with the mod's C#
    /// (speeds, emotes, effects, where choices lead). It also tells the project where its game is.
    /// </summary>
    private void ImportGameDialogue(string path)
    {
        if (_project == null) return;
        var folder = GameFolder.ForLocalizationFile(path);

        List<ImportedDialogue> imported;
        try
        {
            imported = DialogueImporter.Read(File.ReadAllText(path), folder != null ? GameDefinitions.Read(folder) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HjsonException)
        {
            PromptDialog.Inform(this, "Import", $"Couldn't read that file.\n\n{ex.Message}");
            return;
        }
        if (imported.Count == 0)
        {
            PromptDialog.Inform(this, "Import", $"There's no dialogue in that file (nothing under {DialogueExporter.LibraryKey}).");
            return;
        }

        SaveDocument();
        string? firstTitle = null;
        foreach (var dialogue in imported)
        {
            var document = _library.CreateDocument(_project, dialogue.Title, dialogue.Text);
            // If that title was taken, the document says which character it's for.
            if (document.Title != dialogue.Title)
                _library.WriteDocument(document, $"// key: {dialogue.Title}\r\n{dialogue.Text}");
            firstTitle ??= document.Title;
            AddSpeakers(dialogue.Speakers);
        }

        if (folder != null && _project.GameFolder == null) UseGameFolder(folder);
        RefreshDocuments(firstTitle);
        ShowWriting();
        ShowInfo($"Imported {Plural(imported.Count, "character")}: {string.Join(", ", imported.Select(d => d.Title))}.", "OK", () => { });
    }

    private void AddSpeakers(IEnumerable<string> speakers)
    {
        if (_project == null) return;
        _addingCharacters = true; // one redraw at the end, not one per name
        try
        {
            foreach (string speaker in speakers)
            {
                if (!_project.Characters.Any(c => c.AllNames().Contains(speaker, StringComparer.OrdinalIgnoreCase)))
                    _project.Characters.Add(new Character { Name = speaker });
            }
        }
        finally
        {
            _addingCharacters = false;
        }
        Project_FormattingChanged(this, EventArgs.Empty);
    }

    // ── Effects, from the right-click menu ─────────────────────────────────

    /// <summary>Puts words under an effect without having to remember how it's written.</summary>
    private void AddEffectItems(ContextMenu menu)
    {
        if (!_parser.DialogueMarkup || _document == null) return;

        bool canWrap = Editor.SelectionLength > 0 && !Editor.SelectedText.Contains('\n');
        var effects = new MenuItem { Header = "Dialogue effect" };

        var pause = new MenuItem { Header = "Pause here" };
        pause.Click += (_, _) => InsertAtCaret("[]");
        effects.Items.Add(pause);
        effects.Items.Add(new Separator());

        foreach (var (header, word) in EffectMenu)
        {
            var item = new MenuItem { Header = header, IsEnabled = canWrap, ToolTip = canWrap ? null : "Select some words first" };
            item.Click += (_, _) => WrapSelection(word);
            effects.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(effects);
    }

    private void InsertAtCaret(string text)
    {
        Editor.Document.Insert(Editor.CaretOffset, text);
        Editor.Focus();
    }

    private void WrapSelection(string effect)
    {
        int start = Editor.SelectionStart;
        string wrapped = $"[{effect}: {Editor.SelectedText}]";
        Editor.Document.Replace(start, Editor.SelectionLength, wrapped);
        Editor.CaretOffset = start + wrapped.Length;
        Editor.Focus();
    }
}
