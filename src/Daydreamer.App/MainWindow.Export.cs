using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Daydreamer.App.Dialogs;
using Daydreamer.App.Export;
using Daydreamer.Core.Export;
using Daydreamer.Core.Storage;
using Microsoft.Win32;

namespace Daydreamer.App;

// Exporting a document as a PDF, a Word file or plain text, looking the way it does in the editor.
// The work is done in Core/Export (the Word and text files) and App/Export (the PDF).
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private enum ExportFormat { Pdf, Word, Text }

    /// <summary>The Export button in the title bar: a menu of formats for the open document.</summary>
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_document == null) return;
        var menu = new ContextMenu { PlacementTarget = ExportButton, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = $"Export “{_document.Title}” as…", IsEnabled = false });
        foreach (var item in ExportMenuItems(_document)) menu.Items.Add(item);
        menu.IsOpen = true;
    }

    /// <summary>"Export" in a document's right-click menu: its formats are filled in as it opens.</summary>
    private void ExportSubmenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || e.OriginalSource != menu || menu.DataContext is not DocumentInfo document) return;
        menu.Items.Clear();
        foreach (var item in ExportMenuItems(document)) menu.Items.Add(item);
    }

    private List<Control> ExportMenuItems(DocumentInfo document)
    {
        MenuItem Format(string header, string tip, ExportFormat format)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += (_, _) => Export(document, format);
            return item;
        }

        // Checked/Unchecked rather than Click, so it also works when ticked another way (UI automation, say).
        var notes = new MenuItem { Header = "Include notes to yourself", IsCheckable = true, IsChecked = _settings.ExportNotes };
        notes.Checked += (_, _) => SetExportNotes(true);
        notes.Unchecked += (_, _) => SetExportNotes(false);

        return
        [
            Format("PDF (.pdf)", "Looks exactly like the page, on any computer. Good for sharing and printing.", ExportFormat.Pdf),
            Format("Word document (.docx)", "For Word or Google Docs. Each kind of line becomes a style (Dialogue, Caption, SFX…).", ExportFormat.Word),
            Format("Plain text (.txt)", "Just the words, without any formatting.", ExportFormat.Text),
            new Separator(),
            notes,
        ];
    }

    private void SetExportNotes(bool include)
    {
        _settings.ExportNotes = include;
        SaveSettings();
    }

    /// <summary>Asks where to save, then writes the file.</summary>
    private void Export(DocumentInfo document, ExportFormat format)
    {
        if (_project == null) return;

        // The open document is exported as it is right now, even if the last few words aren't saved yet.
        bool isOpen = document.FilePath == _document?.FilePath;
        if (isOpen) SaveDocument();
        string text = isOpen ? Editor.Text : _library.ReadDocument(document);

        var (extension, filter) = format switch
        {
            ExportFormat.Pdf => (".pdf", "PDF document (*.pdf)|*.pdf"),
            ExportFormat.Word => (".docx", "Word document (*.docx)|*.docx"),
            _ => (".txt", "Plain text (*.txt)|*.txt"),
        };
        var dialog = new SaveFileDialog
        {
            Title = "Export",
            FileName = document.Title + extension,
            DefaultExt = extension,
            AddExtension = true,
            OverwritePrompt = true,
            Filter = filter,
            InitialDirectory = Directory.Exists(_settings.ExportFolder)
                ? _settings.ExportFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        string path = dialog.FileName;
        _settings.ExportFolder = Path.GetDirectoryName(path);
        SaveSettings();

        try
        {
            Mouse.OverrideCursor = Cursors.Wait; // a long script can take a moment as a PDF
            var export = ExportDocument.Build(document.Title, text, _project, new ExportOptions(IncludeNotes: _settings.ExportNotes));
            var paper = PaperSize.ForRegion(); // Letter or A4, whichever is usual where you are
            byte[] file = format switch
            {
                ExportFormat.Pdf => PdfWriter.Export(export, paper),
                ExportFormat.Word => DocxExporter.Export(export, paper),
                _ => TextExporter.Export(export),
            };
            File.WriteAllBytes(path, file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PromptDialog.Inform(this, "Export",
                $"Couldn't save the file. If it's open in another program (Word, or a PDF viewer), close it there and try again.\n\n{ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            // Something unexpected in making the file (an unusual font, say). Better a message than losing the app.
            PromptDialog.Inform(this, "Export", $"Something went wrong making the file.\n\n{ex.Message}");
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        ShowInfo($"Exported “{Path.GetFileName(path)}”.", "Open", () => OpenFile(path));
    }

    /// <summary>Opens a file in whatever program Windows uses for it.</summary>
    private void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Win32Exception ex)
        {
            PromptDialog.Inform(this, "Open", $"Windows couldn't open the file.\n\n{ex.Message}");
        }
    }
}
