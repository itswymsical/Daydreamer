using Daydreamer.Core.Models;
using Daydreamer.Core.Storage;

namespace Daydreamer.App;

/// <summary>One recent document on the Home page.</summary>
public sealed class HomeItem
{
    public HomeItem(DocumentInfo document, Project project, string preview)
    {
        Document = document;
        ProjectName = project.Name;
        Styles = project.Styles;
        Characters = project.Characters;
        Preview = preview;
        Modified = document.Modified;
    }

    public DocumentInfo Document { get; }
    public string Title => Document.Title;
    public string ProjectName { get; }
    public StyleSheet Styles { get; }
    public IReadOnlyList<Character> Characters { get; }

    /// <summary>The start of the document, for the page thumbnail.</summary>
    public string Preview { get; }

    public DateTime Modified { get; }

    /// <summary>"Edited 3:42 PM", "Edited yesterday", "Edited Oct 2".</summary>
    public string EditedText
    {
        get
        {
            if (Modified.Date == DateTime.Today) return $"Edited {Modified:t}";
            if (Modified.Date == DateTime.Today.AddDays(-1)) return "Edited yesterday";
            return Modified.Year == DateTime.Today.Year ? $"Edited {Modified:MMM d}" : $"Edited {Modified:MMM d, yyyy}";
        }
    }
}
