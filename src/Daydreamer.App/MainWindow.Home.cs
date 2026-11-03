using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;

namespace Daydreamer.App;

// The Home page: your projects, pinned and recent documents from every project (with page previews), and ways to
// start something new. Clicking a project shows its own page, with all of its documents.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private const int RecentLimit = 15;
    private const int PreviewCharacters = 4000; // plenty for the top of a first page

    /// <summary>The project whose page Home is showing, or null for Home itself.</summary>
    private string? _homeProject;

    private bool IsHomeShown => HomeView.Visibility == Visibility.Visible;

    private void Home_Click(object sender, RoutedEventArgs e) => ShowHome();

    /// <param name="projectPage">Go straight to this project's page instead of the main page.</param>
    private void ShowHome(string? projectPage = null)
    {
        if (TitleBox.IsKeyboardFocused) CommitTitle();
        SaveAll();
        _completion?.Close();
        SetFocusMode(false); // focus mode is for writing; Home always shows everything
        HideInfo();

        _homeProject = projectPage;
        RefreshHome();
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
        ScrollHomeToTop();
    }

    /// <summary>Always open a page at the top (once it's laid out), with its heading in view.</summary>
    private void ScrollHomeToTop()
    {
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
        TitleBox.Visibility = ProjectLabel.Visibility = SaveIcon.Visibility = SaveStatus.Visibility = ExportButton.Visibility = FocusButton.Visibility = writing;
        HomeTitle.Visibility = home ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fills in whichever page Home is on.</summary>
    private void RefreshHome()
    {
        var projects = LoadProjects();
        var page = projects.FirstOrDefault(p => p.Project.Name == _homeProject);
        if (page.Project != null)
        {
            ShowProjectPageFor(page.Project, page.Documents);
            return;
        }

        _homeProject = null;
        HomeMain.Visibility = Visibility.Visible;
        ProjectPage.Visibility = Visibility.Collapsed;

        ProjectList.ItemsSource = projects.Select(p => BuildProjectCard(p.Project, p.Documents)).ToList();

        var everything = projects
            .SelectMany(p => p.Documents.Select(d => (Document: d, p.Project)))
            .OrderByDescending(x => x.Document.Modified)
            .ToList();
        var pinned = everything.Where(x => x.Document.IsPinned).Select(x => BuildItem(x.Document, x.Project)).ToList();
        var recent = everything.Where(x => !x.Document.IsPinned).Take(RecentLimit).Select(x => BuildItem(x.Document, x.Project)).ToList();

        PinnedList.ItemsSource = pinned;
        PinnedSection.Visibility = pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentList.ItemsSource = recent;
        NoRecent.Visibility = recent.Count == 0 && pinned.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowProjectPageFor(Project project, List<DocumentInfo> documents)
    {
        HomeMain.Visibility = Visibility.Collapsed;
        ProjectPage.Visibility = Visibility.Visible;

        ProjectPageTitle.Text = project.Name;
        var latest = documents.OrderByDescending(d => d.Modified).FirstOrDefault();
        string count = documents.Count == 1 ? "1 document" : $"{documents.Count:N0} documents";
        ProjectPageDetails.Text = latest != null ? $"{count}  ·  {HomeItem.EditedOn(latest.Modified)}" : count;

        // ListDocuments already puts pinned documents first; here they get a section of their own.
        var items = documents.Select(d => BuildItem(d, project)).ToList();
        var pinned = items.Where(i => i.IsPinned).ToList();
        var others = items.Where(i => !i.IsPinned).ToList();
        ProjectPinnedList.ItemsSource = pinned;
        ProjectPinnedSection.Visibility = pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectDocumentList.ItemsSource = others;
        ProjectDocumentsHeader.Visibility = others.Count > 0 && pinned.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ProjectPageEmpty.Visibility = documents.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Every project and its documents. A damaged project is left out rather than stopping Home from showing the others.</summary>
    private List<(Project Project, List<DocumentInfo> Documents)> LoadProjects()
    {
        var projects = new List<(Project, List<DocumentInfo>)>();
        foreach (string folder in _library.ListProjectFolders())
        {
            var project = ProjectAt(folder);
            if (project != null) projects.Add((project, _library.ListDocuments(project)));
        }
        return projects;
    }

    /// <summary>The project in a folder. The open project is used as it is: it may have unsaved formatting changes.</summary>
    private Project? ProjectAt(string folder) =>
        _project != null && string.Equals(_project.FolderPath, folder, StringComparison.OrdinalIgnoreCase)
            ? _project
            : TryLoadProject(folder);

    private static HomeItem BuildItem(DocumentInfo document, Project project) => new(document, project, ReadPreview(document));

    private static ProjectCard BuildProjectCard(Project project, List<DocumentInfo> documents)
    {
        var latest = documents.OrderByDescending(d => d.Modified).FirstOrDefault();
        return new ProjectCard(project, documents.Count, latest != null ? BuildItem(latest, project) : null);
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

    // ── A project's page ───────────────────────────────────────────────────

    private void ProjectCard_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ProjectCard card) return;
        _homeProject = card.Name;
        RefreshHome();
        ScrollHomeToTop();
    }

    private void ProjectPageBack_Click(object sender, RoutedEventArgs e)
    {
        _homeProject = null;
        RefreshHome();
        ScrollHomeToTop();
    }

    /// <summary>The project's name under the title goes to its page, from wherever you're writing.</summary>
    private void ProjectLabel_Click(object sender, RoutedEventArgs e)
    {
        if (_project != null) ShowHome(_project.Name);
    }

    /// <summary>A project page's buttons work on that project, which may not be the one that's open yet.</summary>
    private void OpenPageProject()
    {
        if (_homeProject != null && _project?.Name != _homeProject)
            RefreshProjects(_homeProject);
    }

    private void ProjectPageNewDocument_Click(object sender, RoutedEventArgs e)
    {
        OpenPageProject();
        NewDocument();
    }

    private void ProjectPageImport_Click(object sender, RoutedEventArgs e)
    {
        OpenPageProject();
        Import_Click(sender, e);
    }

    private void ProjectPageFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_homeProject == null) return;
        string folder = Path.Combine(_library.RootPath, _homeProject);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // ── Pins ───────────────────────────────────────────────────────────────

    /// <summary>"Pin to top" in a document's right-click menu, in the side list or on a card.</summary>
    private void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        var document = ((FrameworkElement)sender).DataContext switch
        {
            DocumentInfo info => info,
            HomeItem item => item.Document,
            _ => null,
        };
        if (document != null) SetPinned(document, !document.IsPinned);
    }

    /// <summary>Pins live in the document's project, which needn't be the one that's open.</summary>
    private void SetPinned(DocumentInfo document, bool pinned)
    {
        string folder = Path.GetDirectoryName(document.FilePath)!;
        var project = ProjectAt(folder);
        if (project == null) return;

        project.SetPinned(document.Title, pinned);
        try
        {
            _library.SaveProject(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (project == _project) _projectSaveTimer.Start(); // OneDrive may have it locked for a moment
        }

        if (project == _project) RefreshDocuments(_document?.Title);
        if (IsHomeShown) RefreshHome();
    }
}
