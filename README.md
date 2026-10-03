# Daydreamer

A writing app for stories and manga scripts. You write plain text, and Daydreamer recognises
what each line is (dialogue, SFX, captions, panels) and formats it for you. Give a character a
font once, and every one of their lines uses it.

## Running it

You need the .NET 10 SDK (already installed on this machine).

```bash
dotnet run --project src/Daydreamer.App
```

Or open `Daydreamer.sln` in Visual Studio and press F5. Run the tests with `dotnet test`.

On first launch it creates a sample project, **My First Manga**, in
`OneDrive\Documents\Daydreamer` (or `Documents\Daydreamer` if OneDrive isn't set up).

## Writing syntax

| You type                          | It becomes                                                   |
| --------------------------------- | ------------------------------------------------------------ |
| `AKIRA Hello.` or `AKIRA: Hello.` | Dialogue, in AKIRA's style (a defined character, in capitals) |
| `**AKIRA** Hello.`                | Also dialogue: a bold name counts too                        |
| `AKIRA (thought) Hmm.`            | Dialogue + a modifier rule (thought → italic)                |
| `PAGE 1` / `PANEL 1`              | Page and panel markers                                       |
| `CAPTION Tokyo, 2045.`            | Caption (bold by default). `CAPTION:` and `CAPTION 2` work too |
| `CAPTION (AKIRA): I remember`     | Caption in AKIRA's font                                      |
| `SFX KRAKOOM`                     | Sound effect (italic by default). `SFX:` and `FX` work too   |
| `// note`                         | Note to yourself (faded, not counted)                        |
| `# Chapter 1`                     | Heading                                                      |
| `**bold**` / `*italic*`           | Inline emphasis                                              |
| `- item`, `1. item`, `[ ] task`   | Bulleted, numbered and checklist lines                       |
| anything else                     | Description / prose                                          |

You don't have to type any of that, though: the **line menu** at the left of the toolbar
("Description ▾", like Google Docs' "Normal text ▾") sets a line's kind directly. It stores the
choice in a small tag at the start of the line, like `{caption}`, that the editor never shows.
Alignment is stored the same way (`{center}`). The `#`, `!` and `**` you type yourself show
faintly only on the line you're editing, so the page reads like a finished script.

**Keywords** can be renamed per project in Styles → Keywords. A script that says `SCENE 3`
instead of `PANEL 3` just renames the panel keyword, and the whole app follows. Toolbar buttons,
menus, the status bar and the style list all say "Scene". The built-in words keep working too.

An undefined name in ALL CAPS with a colon (`STRANGER: ...`) still counts as dialogue, with an
orange dotted underline. Names that start lots of lines *without* a colon (`KAITO It's late.`)
are suggested in the Characters panel, and **Add all** adds them in one go.

## Formatting

You don't format words one by one; you format **kinds of lines**. Put the cursor on a line and
use the toolbar's **font, size, color and spacing**. The pill on the left of those controls
shows what you're changing:

- On an AKIRA line, it's **AKIRA**: every AKIRA line changes.
- On an SFX line, it's **SFX**: every sound effect changes.
- On an empty line, it's **Default text**, which every other style builds on.

Click the pill to open that style's full settings in the side panel. Text is gray by default
and follows light/dark mode. Color is optional, and colors you pick are automatically nudged
to stay readable on the page.

The spacing button sets **line spacing** and **space between paragraphs** (like Docs' "add space
before paragraph"). **Alignment** works like a word processor instead: it applies to the line
the cursor is on, or the selected lines. To center *every* caption, use the Caption style's
Alignment in the side panel.

**Shortcuts:** type a name in CAPS to get a character pop-up · Ctrl+Space · Ctrl+B / Ctrl+I ·
Ctrl+Shift+L / E / R / J align · Ctrl+N new document · Ctrl+F find · Ctrl+S save (it also
autosaves). Other programs sometimes claim Ctrl+Shift shortcuts for the whole computer (AMD's
Radeon software uses Ctrl+Shift+L, for example), and then they never reach Daydreamer. The
alignment button always works.

## Bringing text in

Import a `.docx` (Word or Google Docs) from the documents list or the Home page, or paste from
Docs or Word:

- Headings become pages, panels and so on (Styles → Importing & pasting). Heading 1 is a page,
  Heading 4 a panel.
- Lists, bold and italic come along, and centered or right-aligned lines stay that way.
- An imported document's **font, size, line spacing and paragraph spacing** become the
  project's, with an Undo button. Fonts packed inside the `.docx` (Google Docs does this) are
  saved into the project's `fonts` folder, so they work even if they aren't installed.
- If the document's panel headings all start with another word (`SCENE 1`, `SCENE 2`), that
  word becomes the panel keyword.
- Speakers are suggested from the text, ready to add as characters.

## Look and feel

- **Light / dark mode:** the sun/moon button in the title bar. It follows Windows at first.
- **Ambient mode (dark mode only):** the ★ button. Soft blue orbs of light drift up over the
  page, under the words. The motion is ported from TrelamiumIII's `GlowMote`.
- **Custom title bar:** drag it, double-click to maximize, and hover the maximize button for
  Windows 11 snap layouts. The window remembers its size and position.

## How the code is organised

```
src/Daydreamer.Core/        ← all the logic; no UI code, so it's easy to test and reuse
  Models/                     Character, TextStyle, StyleSheet, Keywords, Project
  Parsing/ScriptParser.cs     reads one line → "this is AKIRA's dialogue, modifier 'thought'"
  Formatting/StyleResolver    stacks styles: theme gray → Default → element → character → modifier
  Formatting/ThemePalette     the light and dark grays used when no color is chosen
  Editing/                    the line menu (LineConverter), lists, indents, alignment tags
  Import/                     .docx and clipboard HTML → script text, fonts and spacing
  Analysis/DocumentStats      word/page/panel counts, lines per character
  Analysis/SpeakerFinder      spots likely speakers in scripts that don't use colons
  Storage/                    projects are folders, documents are .dream text files

src/Daydreamer.App/         ← the WPF desktop app
  MainWindow.xaml(.cs)        the window, documents, saving
  MainWindow.Writing.cs       line menu, alignment, lists, hidden tags, paste and import
  MainWindow.Formatting.cs    the toolbar's font/size/color/spacing controls
  MainWindow.Home.cs          the Home page and its page previews
  MainWindow.Shell.cs         the custom title bar, maximize, snap layouts, theme + ambient toggles
  FormatTarget.cs             what the toolbar is currently editing
  Editor/ScriptColorizer      hooks the parser + resolver into the editor's drawing
  Editor/MarkupHider          hides tags, "# " and ** (live preview)
  Editor/ListMarkerGenerator  draws bullets and clickable checkboxes
  Editor/WpfStyles            fonts (incl. project fonts), colors, line spacing fonts
  Theming/ThemeManager        swaps light/dark (Themes/Light.xaml ↔ Themes/Dark.xaml)
  Ambient/                    the drifting lights (GlowMote, OrbSprites, AmbientField)
  Controls/                   StyleEditor, ColorPicker, DocumentPreview

third-party/AvalonEdit/     ← the editor control, as source (MIT), with small marked changes
                              ("Daydreamer:") for per-line alignment, list indents and
                              paragraph spacing

tests/                      ← `dotnet test` runs both projects
```

**A good reading order to understand how it works:**

1. `ScriptParser.cs`: how a line of text is recognised.
2. `StyleResolver.cs`: how the final look is worked out by stacking styles.
3. `ScriptColorizer.cs`: about 40 lines that apply that look in the editor. The text itself is
   never changed. Formatting happens only at draw time, which is why restyling is instant.

### Why plain text?

The formatting is never saved into your documents; only the rules are (in `project.json`).
That's what makes it dynamic. It also means your writing is always readable in Notepad,
never gets corrupted by a formatting bug, and works well with OneDrive and version control.

### How line spacing works

WPF decides how tall a line is from its font, so `WpfStyles.Family` wraps the real font in a
**composite font**. It draws exactly the same letters but reports taller lines, with the text
centred in the extra space. Because it's part of the font, wrapped lines get the spacing too.

### How the ambient lights glow

Each orb is a small pre-drawn image (like a texture in a game). The images are turned into
*pure light* (`OrbSprites.AsLight`): they keep their color but have zero alpha, so WPF adds them
to the page instead of painting over it. That's the same trick as an additive blend state.
A full-screen HLSL pixel shader produced the same result but made WPF redraw the whole page
every frame (about 20% of a CPU core), while the baked images cost about 4%.

## Where your files live

```
Daydreamer/
  My First Manga/
    project.json      characters, style rules, keywords
    Chapter 1.dream   your script (plain UTF-8 text)
    fonts/            fonts that came packed in imported documents
    .trash/           deleted documents (recoverable)
```

Settings (theme, ambient mode, window position, last open document) are in
`%AppData%\Daydreamer\settings.json`.

## Developer notes

- `Daydreamer.App.exe --library <folder>` opens a different library without changing your
  settings, and `--theme light|dark` forces a theme. Both are handy for testing.
- Debug builds also accept `--screenshot out.png`, which renders the window to an image and exits.
- The editor control is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit). The Windows 11
  look comes from WPF's built-in Fluent theme.
- Planned features are in [TODO.md](TODO.md).
