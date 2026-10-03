using Daydreamer.Core.Models;

namespace Daydreamer.Core.Storage;

/// <summary>A small starter project so the formatting can be seen in action on first launch.</summary>
public static class SampleContent
{
    public const string ProjectName = "My First Manga";

    public const string ChapterText = """
        # Chapter 1: The Last Train

        // Lines starting with // are notes to yourself. They show up faded and aren't counted.
        // Click any AKIRA line and pick a new font in the toolbar — every AKIRA line updates at once.

        PAGE 1

        PANEL 1
        Wide shot. An empty station platform at midnight. A single flickering light.

        CAPTION: Neo-Kyoto. The year everything stopped.

        SFX: bzzt... bzzt...

        PANEL 2
        AKIRA sits alone on a bench, clutching a crumpled ticket.

        AKIRA: It's late. Too late.
        AKIRA (thought): Why did I ever agree to this?

        PANEL 3
        A train screams into the station, out of nowhere.

        SFX: KRRRRSHHHHH

        PAGE 2

        PANEL 1
        The doors hiss open. MIKA leans out, grinning.

        MIKA (shout): Are you getting on or **what**?
        AKIRA: You're... you're supposed to be *gone*.
        MIKA: Details.

        PANEL 2
        A shadowy figure watches from the end of the platform.

        STRANGER (whisper): Found you.
        // STRANGER isn't a character yet, so the name is underlined in orange.
        // Right-click the line and choose "Add STRANGER as a character" to fix that.
        """;

    public static Project CreateSampleProject(Library library)
    {
        var project = library.CreateProject(ProjectName);

        project.Characters.Add(new Character
        {
            Name = "AKIRA",
            Aliases = "AKI",
            Notes = "Our hero. Always running late.",
            Style = new TextStyle { FontFamily = "Georgia" },
        });
        project.Characters.Add(new Character
        {
            Name = "MIKA",
            Notes = "Supposed to be gone. Isn't.",
            Style = new TextStyle { FontFamily = "Comic Sans MS" },
        });

        library.SaveProject(project);
        library.CreateDocument(project, "Chapter 1", ChapterText);
        return project;
    }
}
