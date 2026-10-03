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

| You type                      | It becomes                                                        |
| ----------------------------- | ----------------------------------------------------------------- |
| `# Chapter 1`                 | Heading                                                           |
| `PAGE 1`                      | Page marker                                                       |
| `PANEL 1`                     | Panel marker                                                      |
| `AKIRA: Hello.`               | Dialogue, in AKIRA's style                                        |
| `AKIRA (thought): Hmm.`       | Dialogue + a modifier rule (thought → italic)                     |
| `CAPTION: Tokyo, 2045.`       | Caption (bold by default)                                         |
| `CAPTION (AKIRA): I remember` | Caption in AKIRA's font                                           |
| `SFX: KRAKOOM`                | Sound effect (italic by default)                                  |
| `// note`                     | Note to yourself (faded, not counted)                             |
| `**bold**` / `*italic*`       | Inline emphasis                                                   |
| anything else                 | Description / prose                                               |

An undefined name in ALL CAPS (`STRANGER: ...`) still counts as dialogue, but gets an orange
dotted underline. Right-click the line, or click the name in the Characters panel, to add them.

## Formatting

You don't format words one by one; you format **kinds of lines**. Put the cursor on a line and
use the toolbar's **font, size, color and line spacing**. The pill on the left of those controls
shows what you're changing:

- On an AKIRA line, it's **AKIRA**: every AKIRA line changes.
- On an SFX line, it's **SFX**: every sound effect changes.
- On an empty line, it's **Default text**, which every other style builds on.

Click the pill to open that style's full settings in the side panel. Text is gray by default
and follows light/dark mode. Color is optional, and colors you pick are automatically nudged
to stay readable on the page.

**Shortcuts:** type a name in CAPS to get a character pop-up · Ctrl+Space · Ctrl+B / Ctrl+I ·
Ctrl+N new document · Ctrl+F find · Ctrl+S save (it also autosaves).

## Look and feel

- **Light / dark mode:** the sun/moon button in the title bar. It follows Windows at first.
- **Ambient mode (dark mode only):** the ★ button. Soft blue orbs of light drift up over the
  page, under the words. The motion is ported from TrelamiumIII's `GlowMote`.
- **Custom title bar:** drag it, double-click to maximize, and hover the maximize button for
  Windows 11 snap layouts. The window remembers its size and position.

## How the code is organised

```
src/Daydreamer.Core/        ← all the logic; no UI code, so it's easy to test and reuse
  Models/                     Character, TextStyle, StyleSheet, Project
  Parsing/ScriptParser.cs     reads one line → "this is AKIRA's dialogue, modifier 'thought'"
  Formatting/StyleResolver    stacks styles: theme gray → Default → element → character → modifier
  Formatting/ThemePalette     the light and dark grays used when no color is chosen
  Analysis/DocumentStats      word/page/panel counts, lines per character
  Storage/                    projects are folders, documents are .dream text files

src/Daydreamer.App/         ← the WPF desktop app
  MainWindow.xaml(.cs)        the window, documents, saving
  MainWindow.Formatting.cs    the toolbar's font/size/color/line spacing controls
  MainWindow.Shell.cs         the custom title bar, maximize, snap layouts, theme + ambient toggles
  FormatTarget.cs             what the toolbar is currently editing
  Editor/ScriptColorizer      hooks the parser + resolver into the editor's drawing
  Editor/WpfStyles            fonts, colors, readable-color adjustment, line spacing fonts
  Theming/ThemeManager        swaps light/dark (Themes/Light.xaml ↔ Themes/Dark.xaml)
  Ambient/                    the drifting lights (GlowMote, OrbSprites, AmbientField)
  Controls/                   StyleEditor, ColorPicker

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
    project.json      characters + style rules
    Chapter 1.dream   your script (plain UTF-8 text)
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
