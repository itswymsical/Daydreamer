using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;

namespace Daydreamer.App;

// The Home page: recent documents from every project (with page previews) and ways to start something new.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private const int RecentLimit = 15;
    private const int PreviewCharacters = 4000; // plenty for the top of a first page

    private bool IsHomeShown => HomeView.Visibility == Visibility.Visible;

    private void Home_Click(object sender, RoutedEventArgs e) => ShowHome();

    private void ShowHome()
    {
        if (TitleBox.IsKeyboardFocused) CommitTitle();
        SaveAll();
        _completion?.Close();
        HideInfo();

        var items = BuildHomeItems();
        RecentList.ItemsSource = items;
        NoRecent.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HomeGreeting.Text = DateTime.Now.Hour switch
        {
            >= 5 and < 12 => "Good morning",
            >= 12 and < 17 => "Good afternoon",
            _ => "Good evening",
        };
        NewDocumentHint.Text = _project != null ? $"In {_project.Name}" : "Start writing";

        HomeView.Visibility = Visibility.Visible;
        WritingView.Visibility = Visibility.Collapsed;
        SetTitleBarForHome(true);
        Title = "Daydreamer";
        UpdateAmbient();

        // Always open Home at the top (once it's laid out), with the greeting in view.
        HomeView.ScrollToTop();
        Dispatcher.BeginInvoke(() => HomeView.ScrollToTop(), DispatcherPriority.Loaded);
    }

    /// <param name="focusEditor">False when something else (like the title box) should get the keyboard.</param>
    private void ShowWriting(bool focusEditor = true)
    {
        if (!IsHomeShown) return;
        HomeView.Visibility = Visibility.Collapsed;
        WritingView.Visibility = Visibility.Visible;
        SetTitleBarForHome(false);
        Title = _document != null ? $"{_document.Title} — Daydreamer" : "Daydreamer";
        UpdateAmbient();
        if (focusEditor) Dispatcher.BeginInvoke(() => Editor.Focus(), DispatcherPriority.Input);
    }

    private void SetTitleBarForHome(bool home)
    {
        var writing = home ? Visibility.Collapsed : Visibility.Visible;
        TitleBox.Visibility = ProjectLabel.Visibility = SaveIcon.Visibility = SaveStatus.Visibility = writing;
        HomeTitle.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The most recently edited documents across all projects, newest first.</summary>
    private List<HomeItem> BuildHomeItems()
    {
        var documents = new List<(DocumentInfo Document, Project Project)>();
        foreach (string folder in _library.ListProjectFolders())
        {
            var project = _project != null && string.Equals(_project.FolderPath, folder, StringComparison.OrdinalIgnoreCase)
                ? _project // the open project may have unsaved formatting changes
                : TryLoadProject(folder);
            if (project == null) continue;
            documents.AddRange(_library.ListDocuments(project).Select(d => (d, project)));
        }

        return documents
            .OrderByDescending(x => x.Document.Modified)
            .Take(RecentLimit)
            .Select(x => new HomeItem(x.Document, x.Project, ReadPreview(x.Document)))
            .ToList();
    }

    private Project? TryLoadProject(string folder)
    {
        try
        {
            return _library.LoadProject(folder);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null; // a damaged project shouldn't stop Home from showing the others
        }
    }

    private static string ReadPreview(DocumentInfo document)
    {
        try
        {
            using var reader = new StreamReader(document.FilePath);
            var buffer = new char[PreviewCharacters];
            int read = reader.ReadBlock(buffer, 0, buffer.Length);
            return new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private void RecentDocument_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not HomeItem item) return;
        if (_project?.Name == item.ProjectName)
            RefreshDocuments(item.Title);
        else
            RefreshProjects(item.ProjectName, item.Title);
        ShowWriting();
    }

    private void HomeNewDocument_Click(object sender, RoutedEventArgs e) => NewDocument();

    private void HomeNewProject_Click(object sender, RoutedEventArgs e)
    {
        if (CreateProject()) ShowWriting();
    }
}
