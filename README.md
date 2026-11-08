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

## Projects and pins

The Home page lists your **projects** above your recent documents. Click one to see everything in it, with page
previews, and to start or import a document there. The project's name under a document's title goes to the same
page from wherever you're writing.

**Right-click a document** (in the side list or on a card) and pick **Pin to top**. Pinned documents stay first in
the document list and get their own row on Home and on the project's page. Pins are saved with the project.

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

**Your own styles:** for lines that don't fit the usual kinds, like a letter or an inscription. Make one in
Styles → Your styles (or from the line menu), for **this document** or **the whole project**, and put lines in it from
the line menu. It goes on top of whatever the line already is, so a character's line in the Letter style is still
theirs. It's kept in the line's hidden tag (`{style:Letter}`), and a Word export gets a Word style of that name.

**Shortcuts:** type a name in CAPS to get a character pop-up · Ctrl+Space · Ctrl+B / Ctrl+I ·
Ctrl+Shift+L / E / R / J align · Ctrl+N new document · Ctrl+F find · Ctrl+S save (it also
autosaves) · Ctrl+\ hide the side panel · F11 focus mode. Other programs sometimes claim Ctrl+Shift shortcuts for the whole computer (AMD's
Radeon software uses Ctrl+Shift+L, for example), and then they never reach Daydreamer. The
alignment button always works.

## Spelling

Misspelled words get a wavy red line, like in Word or Google Docs. The checking is done by
Windows' own spell checker, so it works offline and your writing never leaves your PC.

- **Right-click** a marked word for corrections, **Ignore** (until Daydreamer closes), or **Add to
  this story's dictionary**. That saves the word in the project, so made-up places and slang stay
  known on any PC.
- It understands scripts. Only the words people read are checked: dialogue, captions, descriptions,
  headings and lists. Speaker names, PAGE and PANEL lines, SFX, notes and hidden formatting are left
  alone. Your characters' names are always known, and words in CAPITALS (names, shouting, sound
  words) are never marked.
- A word isn't marked while you're still typing it, only once you've moved on.
- The status bar shows how many mistakes there are; click it to jump to the next one.
- Turn it off (or back on) with **Check spelling** in the right-click menu.

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

## Exporting

The **Export** button in the title bar (or right-click a document → **Export**) saves a copy as:

- **PDF**: exactly like the page in light mode, on any computer. The fonts are packed in (just the letters
  used, including fonts in the project's `fonts` folder), and the text stays real text you can select,
  search and copy.
- **Word document (.docx)**: for Word, Google Docs or LibreOffice. Each kind of line becomes a Word style
  (Dialogue, Caption, SFX, and one per character, like "Dialogue – AKIRA"), so changing the Caption style in
  Word changes every caption. Pages and panels become Word headings (the reverse of Styles → Importing),
  lists are real Word lists, and importing the file again gives you your pages, panels and lists back.
- **Plain text (.txt)**: just the words as the page shows them, without hidden tags or `**`.

Notes to yourself are left out unless you tick **Include notes to yourself** in the menu. The paper is
Letter or A4 (whichever is usual where your computer is set up), with 1-inch margins. A Word file names its
fonts but doesn't pack them in, so a computer without them shows a stand-in font; a PDF always has them.

## Game dialogue

Daydreamer can write a game's dialogue: **one document per character**, holding everything they'll ever say.
**Export → Send to the game** turns it into the two files the mod needs, each in the right place. It was built for
TrelamiumIII's dialogue system (`Dialogue.Define`, `DialogueLibrary` localization).

```
// speed: 1.1                                   the character's profile, above the first heading (all optional)
// tone worried: emote 3, speed 0.9

// Meeting them for the first time.             a note becomes a # comment in the HJSON
# Greeting                                      a conversation: Silvian.Greeting, in Silvian's document
SILVIAN: Well,[] howdy![30] Name's Silvian.     a line, with pauses
SILVIAN (worried): You [shake 0.6: alright]?    a tone for the line, an effect on some of its words
- I'm fine. -> Greeting_True                    a choice, and the conversation it leads to
- Who's asking?                                 a choice whose response is written right under it…
    Just a neighbor, [player].                  …indented, which makes it a conversation of its own
- ... -> Greeting_Silent (dead)                 (dead): the answer that means no
// unlock: HerbalForager                        what happens when the conversation ends
```

Without a name, a line belongs to whoever spoke last in the conversation, or else the document's character.

| You write                               | What it does in the game                             |
| --------------------------------------- | ---------------------------------------------------- |
| `[]`                                    | A pause that fits its punctuation (14 to 28 frames)  |
| `[30]`                                  | `new Pause(30)`                                      |
| `[shake 0.6: words]`, `[wave: words]`   | `new Shake(0.6f)`, `new Wave()`                      |
| `[slow 2: words]`, `[fast: words]`      | `new Slow(2f)`, `new Fast()`                         |
| `[pitch 1.2: words]`                    | `new Pitch(1.2f)`                                    |
| `[up 0.3: words]`, `[down: words]`      | `Inflection.Up(0.3f)`, `Inflection.Down()`           |
| `[question: words]`, `[emphasis 0.25 lift 2: words]` | `Inflection.Question()`, `Inflection.Emphasis(0.25f, lift: 2f)` |
| `[words]`                               | The same as `[emphasis: words]`                      |
| `[waver: words]`                        | `Inflection.Waver()`                                 |
| `[inflect to 1.4 steps 4 lift 3: words]` | `new Inflection(1.4f, steps: 4, lift: 3f)`          |
| `[sound MenuTick:]`                     | `new Sound(SoundID.MenuTick)`                        |
| `[none: words]`                         | Plain words                                          |
| `[player]`                              | The player's name (`[speakerName:player]`)           |
| `(slow)`, `(fast)`, `(speed 0.8)`, `(emote 3)`, `(worried)` | The line's speed and emote: `SILVIAN (slow): …`, or `(slow) …` on a line with no name |

Select some words and right-click → **Dialogue effect** to add one without typing it. In a project with a game folder,
pauses and effect marks only show on the line you're editing, and words with an effect are colored.

**Notes that do something**, inside a conversation: `// unlock: Job`, `// handin: Job` (under a line's two choices),
`// complete: Some.Code()`, `// hand-written`. On a choice: `(run: Some.Code())`. **In the profile:** `speed`,
`tone name`, `speaker: Goblin Tinkerer`, `key: Tinkerer` (when the title isn't the key), `using: Some.Namespace` (for
your code), `color: 255 110 80`, and `pauses: auto` (a pause after every comma and sentence on lines without any).

**What sending does:**

- **The words** go into the localization file that already has that character, or a new
  `Localization\DialogueLibrary\<Name>\en-US_Mods.<Mod>.hjson`. Only the document's conversations change; the rest of
  the file stays as it was, comments included, written the way tModLoader writes it so a diff shows only what changed.
- **The flow** goes in `Core\Dialogue\Definitions\Written\<Name>WrittenDialogue.cs`, replaced whole on every send. Your
  hand-written files are never touched. It's a `ModSystem`, so it registers after every `ModDialogue`, and because the
  registry keeps the last definition of a key, a conversation written here takes over from a hand-written one. No
  registry or profile document is needed: it's picked up automatically like any other class in the mod.
- **Speakers:** a character without a profile in `SpeakerRegistry` speaks in gray with no portrait or voice, and you're
  told. `// color:` gives a new one a generated profile with that color. Portraits and voices still go in `SpeakerRegistry`.
- **Before anything changes**, it warns about unknown effects and tones, choices that lead nowhere, and hand-written
  conversations whose anchors no longer match their code. It asks before removing conversations the game has and the
  document doesn't (keeping them is the default). Replaced files are copied to the project's `.trash\Game exports` first.

**Bringing existing dialogue in:** import a localization `.hjson`. You get one document per character, with speeds,
emotes, effects, pause lengths and branches read from the mod's C#. Exporting it again gives the same words and flow.
A conversation whose code can't be carried over is marked `// hand-written`: its words export, and its code stays in
charge. That's one built by a helper method (`DefineLost`), or one that calls a method private to its class (`GiveLetter`).
Remove the mark, and fill in its choices, to bring it across.

Only `en-US`, `DialogueLibrary` and `BookLibrary` are handled. Other localization is still written by hand.

### Game books

A book is **one document**, and `// book:` at the top is what makes it one. **Export → Send to the game** sends it to
the mod's `BookLibrary`, the same way dialogue goes to `DialogueLibrary`.

```
// book: Lore.Tides                             the book's key in the mod (the Key of its ModLoreBook)

# The Tides                                     the title: BookLibrary.Lore.Tides.Title

## Low Water                                    a chapter, numbered for you: Ch1.Title
It was cold.                                    words right under a heading: Ch1.Opening

It got colder.                                  a blank line is a new paragraph, [p] in the game's text
Much colder.                                    a single line break stays a line break

## Ch7: High Water                              a chapter with a key of its own
### Excerpt                                     a block with a name of its own: Ch7.Excerpt
A line worth quoting.
### /ContentsHeader                             a leading slash means the top of the book: ContentsHeader
Contents

// Check this with the map.                     a note is left out of the words; above a heading, it's a # comment in the HJSON
\# Not a heading                                a backslash at the start of a line takes it literally
\                                               …and alone, it's an empty line that isn't a new paragraph
```

The game draws `[p]` and a line break the same way, and tags like `[gloss:Silhouette]` are just written in the words.

**The names matter:** a block's name is the key the book's C# asks for (`.Prose("Ch1.Second")`), and they aren't always
under the chapter. `Lore.CorridorOfDarkness` reads `Prose` from the top of the book, right after its chapter heading, and
that's what `### /Prose` is for.

**What sending does:**

- **The words** go in the file that already has the book, or else `Localization\BookLibrary\en-US_Mods.<Mod>.hjson`.
  Only the document's own blocks change. Other books, `LoreGlossary`, `LoreUI` and comments stay as they were, and a block
  the game has but the document doesn't stays too, unless you say to remove it when asked.
- **The layout** stays in the mod's class if it has one for that key (`Core\Books\Lore\Definitions`), and you're warned about
  any words that class doesn't ask for, because they'd never reach the page. It can follow a helper like
  `Chapter(book, "Ch1")`; if the keys are built some other way, it doesn't warn.
- **A book with no class** won't appear in the game, so you're asked whether to add one. It goes in
  `Core\Books\Lore\Definitions\Written\<Key>Book.cs` and is replaced on every send: a title page with contents if there's a
  `ContentsHeader`, then each block in order. `Excerpt…` becomes a quote, and `AsideTitle` with `AsideBody` a panel. To
  take over the look (cover, palette, fonts), move the class out of `Written`. Your own class always wins, and the
  generated one is then removed.
- **It won't send** a key that would land on other books: `Misc` when `Misc` holds books, or `Misc.Diary.Extra` inside one.
  Replaced files are copied to the project's `.trash\Game exports` first.

**Bringing existing books in:** import the `BookLibrary` `.hjson`, and you get one document per book, with every key
kept. Sending one straight back changes nothing, down to the byte.

## Music

The **Music** tab in the side panel (or the ♪ in the title bar) is a music player for writing to.

- **Add songs** copies MP3, M4A/AAC, WAV, WMA or FLAC files into Daydreamer's own `Music` folder.
  You can also drag files, or a whole folder, onto the list. Your stories keep their music even if
  the originals are moved or deleted, and the folder syncs with OneDrive like everything else.
- **Playlists:** make as many as you like, and put a song in several. Right-click a song to add it to
  one, and drag songs to reorder them. *All songs* is everything you've added.
- Songs **crossfade** into each other. Set how long (Off to 12 seconds) at the bottom of the tab.
- Right-click a song and pick **Cut in line now** to fade straight into it; the playlist carries on
  after it. **Play next** waits for the current song to end instead. (Music cues for scenes will
  cut in line the same way.)
- While something plays, the title bar shows it with previous / pause / next buttons, and the
  keyboard's media keys control it.

## Look and feel

- **Focus mode:** the ⛶ button in the title bar, or F11. The toolbar and both panels go away so
  it's just you and the page; Esc or F11 brings them back.
- **Hide the side panel:** the button at the end of the toolbar, or Ctrl+\. It stays hidden next
  time too. It comes back by itself when you ask for something in it (a character's settings,
  a style, the Music tab).
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
  Export/ExportDocument       a document worked out for paper: every line's words and looks, markup gone
  Export/DocxExporter         … written as a Word file (styles per kind of line, real lists)
  Export/TextExporter         … or as plain text
  Game/DialogueBook           a character's document, understood: conversations, lines, choices, tones
  Game/DialogueEffects        [shake 0.6: words] ↔ new Shake(0.6f)
  Game/DialogueExporter       … written as HJSON entries and a C# definition
  Game/GameExporter           where it all goes in the mod, and merging it in
  Game/GameDefinitions        reads what the mod's C# already defines
  Game/BookDocument           a book's document, understood, and an existing book written back out as one
  Game/BookExporter           where a book goes in the mod, merging it in, and its generated class
  Game/DialogueImporter       existing dialogue → a document
  Game/Hjson                  reads and writes HJSON the way tModLoader does
  Parsing/DialogueSyntax      finds the marks in game dialogue, for the editor to hide and color
  Analysis/DocumentStats      word/page/panel counts, lines per character
  Analysis/SpeakerFinder      spots likely speakers in scripts that don't use colons
  Storage/                    projects are folders, documents are .dream text files
  Proofing/ScriptSpellChecker spell checking that skips names, SFX, notes and markup
  Music/MusicStore            the Music folder: copying songs in, playlists, music.json
  Music/MusicChannel          what plays when: the playlist, crossfades, songs cutting in line
  Music/AudioTags             reads a song's title and artist from its MP3 tags

src/Daydreamer.App/         ← the WPF desktop app
  MainWindow.xaml(.cs)        the window, documents, saving
  MainWindow.Writing.cs       line menu, alignment, lists, hidden tags, paste and import
  MainWindow.Formatting.cs    the toolbar's font/size/color/spacing controls
  MainWindow.Home.cs          the Home page, project pages, pins and page previews
  MainWindow.Shell.cs         the custom title bar, maximize, snap layouts, theme + ambient toggles
  MainWindow.Layout.cs        focus mode, and hiding the side panel
  MainWindow.Spelling.cs      the wavy underlines, right-click fixes, the mistake count
  MainWindow.Export.cs        the Export menu: asks where to save, then writes the file
  MainWindow.Game.cs          sending to the game, importing dialogue, the effect menu
  MainWindow.Styles.cs        styles you make yourself
  Export/PageLayout           puts an exported document on pages with WPF's text engine
  Export/PdfWriter, PdfFont   turns those pages into a PDF, packing in the fonts
  Editor/WindowsSpellChecker  talks to Windows' spell checker (COM)
  Editor/SpellingUnderliner   draws the wavy lines
  MainWindow.Music.cs         the Music tab and the little player in the title bar
  Music/                      WPF's MediaPlayer as the channel's two "decks"; measuring song lengths
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

### How PDF export works

There's no PDF library: WPF already does the hard parts. `PageLayout` breaks each line into rows with
`TextFormatter` (the engine the editor itself draws with, so wrapping, spacing and stand-in fonts for Japanese
match the screen) and draws every page into a WPF `Drawing`. `PdfWriter` then walks each drawing: a run of
letters becomes a PDF text instruction that names glyphs by number, and an underline becomes a filled shape.
Each font is packed in by `GlyphTypeface.ComputeSubset` (WPF's own subsetter, which keeps just the letters
used), along with a small table of which character each glyph is, so text copied from the PDF comes out right.

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
    project.json      characters, style rules, keywords, pins, your styles, the game folder
    Chapter 1.dream   your script (plain UTF-8 text)
    fonts/            fonts that came packed in imported documents
    .trash/           deleted documents (recoverable), and files replaced by sending to the game
  Music/              Daydreamer's copies of your songs
    music.json        the song list (titles, artists, lengths) and your playlists
    .trash/           deleted songs (recoverable)
```

Settings (theme, ambient mode, music volume and crossfade, window position, last open document) are in
`%AppData%\Daydreamer\settings.json`.

## Developer notes

- `Daydreamer.App.exe --library <folder>` opens a different library without changing your
  settings, and `--theme light|dark` forces a theme. Both are handy for testing.
- Debug builds also accept `--screenshot out.png`, which renders the window to an image and exits.
- The app icon (`src/Daydreamer.App/Daydreamer.ico`) is drawn from the title bar's logo by
  `tools/MakeIcon.ps1`. Run it again if the logo changes.
- The editor control is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit). The Windows 11
  look comes from WPF's built-in Fluent theme.
- Planned features are in [TODO.md](TODO.md).
