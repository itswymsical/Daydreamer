using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;

namespace Daydreamer.App;

/// <summary>One document on the Home page or a project's page.</summary>
public sealed class HomeItem
{
    public HomeItem(DocumentInfo document, Project project, string preview)
    {
        Document = document;
        ProjectName = project.Name;
        Styles = project.Styles;
        Characters = project.Characters;
        Keywords = project.Keywords;
        CustomStyles = project.StylesFor(document.Title).ToList();
        DialogueMarkup = project.GameFolder != null;
        Preview = preview;
        Modified = document.Modified;
    }

    public DocumentInfo Document { get; }
    public string Title => Document.Title;
    public bool IsPinned => Document.IsPinned;
    public string ProjectName { get; }
    public StyleSheet Styles { get; }
    public IReadOnlyList<Character> Characters { get; }
    public Keywords Keywords { get; }
    public IReadOnlyList<NamedStyle> CustomStyles { get; }
    public bool DialogueMarkup { get; }

    /// <summary>The start of the document, for the page thumbnail.</summary>
    public string Preview { get; }

    public DateTime Modified { get; }

    public string EditedText => EditedOn(Modified);

    /// <summary>"Edited 3:42 PM", "Edited yesterday", "Edited Oct 2".</summary>
    public static string EditedOn(DateTime when)
    {
        if (when.Date == DateTime.Today) return $"Edited {when:t}";
        if (when.Date == DateTime.Today.AddDays(-1)) return "Edited yesterday";
        return when.Year == DateTime.Today.Year ? $"Edited {when:MMM d}" : $"Edited {when:MMM d, yyyy}";
    }
}

/// <summary>One project on the Home page: its name, how much is in it, and its latest document as a cover.</summary>
public sealed class ProjectCard
{
    public ProjectCard(Project project, int documentCount, HomeItem? latest)
    {
        Name = project.Name;
        DocumentCount = documentCount;
        Latest = latest;
    }

    public string Name { get; }
    public int DocumentCount { get; }

    /// <summary>The most recently edited document, or null for an empty project.</summary>
    public HomeItem? Latest { get; }

    /// <summary>"24 documents  ·  Edited Oct 2"</summary>
    public string Details
    {
        get
        {
            string count = DocumentCount == 1 ? "1 document" : $"{DocumentCount:N0} documents";
            return Latest != null ? $"{count}  ·  {Latest.EditedText}" : count;
        }
    }
}
