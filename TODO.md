# Daydreamer to-do

Features to add, roughly in the order they were asked for. Each note says how it could fit
Daydreamer's way of working: plain text underneath, with formatting applied by rules.

## Formatting

- [x] **Line spacing.** Single / 1.15 / 1.5 / Double / custom, per kind of line (toolbar or side panel).
- [x] **Space between paragraphs.** In the spacing menu and each style's settings; imported from Docs/Word.
- [x] **Alignment: left, center, right, justify.** Per line from the toolbar (Ctrl+Shift+L/E/R/J), or
      for a whole kind of line in the side panel.
- [x] **Clear formatting button.** Strips `**` / `*` from the selected lines.
- [x] **Increase / decrease indent.** Toolbar buttons, and Tab / Shift+Tab on list items.

## Lists

- [x] **Bulleted list:** lines starting with `- ` or `* `.
- [x] **Numbered list:** lines starting with `1. `, renumbered automatically.
- [x] **Checklist:** lines starting with `[ ] ` / `[x] `, with a clickable box.

## Links and structure

- [ ] **Hyperlinks:** recognise web addresses (and maybe `[text](link)`), Ctrl+click to open.
- [ ] **Horizontal lines:** a line of `---` drawn as a divider.
- [ ] **Page breaks:** an explicit break for prose and printing (scripts already have `PAGE` markers).
- [ ] **Bookmarks:** jump to headings, pages, or marked spots from a sidebar outline.

## Pages and view

- [ ] **Page numbers:** shown when printing/exporting (and maybe in the margin while writing).
- [x] **Focus mode:** F11 (or the title bar button) hides the sidebars and toolbar so it's just you
      and the page. The side panel can also be hidden on its own (Ctrl+\).

## Importing

- [x] Headings, lists, bold/italic, alignment, fonts, sizes and spacing from .docx and pasted HTML.
- [x] Fonts packed inside a .docx are kept with the project.
- [x] Speakers suggested from scripts that don't use colons.
- [ ] Heading sizes and fonts (only the body text's look is imported so far).

## Music

- [x] **Music library and playlists.** Songs are copied into the library's `Music` folder; playlists,
      crossfades, "cut in line", the title bar player and media keys.
- [ ] **Shuffle (and repeat modes).** Shuffle would deal the playlist into a random order once and play
      through it (so nothing repeats until everything has played). Repeat: all (as now), one, or off.
      It fits in `MusicChannel.TakeNext`, the one place that picks the next song.
- [ ] **Music cues for pages and scenes.** The plan:
      - Pick a song for a `PAGE` or `SCENE` line from its right-click menu. It's stored as a hidden tag
        on that line, like `{center}`: `{music:Scene 7 Theme}`. The text stays plain, and the tag
        shows faintly on the line you're editing, with a small ♪ in the margin.
      - When you reach that page or scene, the cue **cuts in line**: the channel crossfades into it
        (`MusicChannel.PlayNow`, which already works this way) and the playlist carries on afterwards.
      - To decide: does moving the cursor trigger a cue, scrolling, or only a "read-through" mode? Does a
        cue play once, or loop until the next cue? Does it fade back to the playlist when you leave the
        scene, or only when the song ends?

## Proofreading

- [x] **Spelling**, with Windows' own spell checker: offline, script-aware (skips names, SFX, notes and
      markup), right-click fixes, a dictionary per story, and a mistake count that jumps to the next one.
- [ ] **Grammar** with [Harper](https://github.com/Automattic/harper): offline, private, fast, English
      only. It would run as a small helper program (`harper-ls`) next to Daydreamer and draw blue
      underlines with the same right-click menu. Dialogue should be checked more gently than captions
      and descriptions (people talk in fragments).
- [ ] Maybe: a **"Proofread this page"** button that asks Claude about tone and dialogue (online,
      needs an API key, costs a little per use).

## Organising

- [x] **Projects on the Home page**, each with its own page of documents.
- [x] **Pinned documents**, first in the document list and in their own row on Home.
- [x] **Your own styles**, for one document or a whole project.
- [ ] Maybe: folders inside a project. A project is one flat folder of documents for now.

## Game dialogue

- [x] **One document per character** → HJSON localization and a C# definition, with tones, speed and effects in the text.
- [x] **Importing** existing localization and definitions into documents.
- [ ] Follow helper methods like `DefineLost` when importing, so fewer conversations stay `// hand-written`.
- [x] **One document per book** → the mod's `BookLibrary` localization, merged so only that book's own blocks change,
      with a generated class for a book the mod doesn't define yet. Importing a real book and sending it back leaves the file byte for byte.
- [ ] Read a hand-written book's block order and styles when importing, so it could be laid out from the document too.
- [ ] `LoreGlossary` (the terms behind `[gloss:…]`), which sits beside the books in the same file.
- [ ] Other localization: `SilvianCounsel`, item text.
- [ ] Other languages than `en-US`.

## Bigger ideas from earlier

- [x] **Export** to PDF, Word (.docx) and plain text, using the same formatting rules so exports match the
      screen. Notes to yourself are left out unless asked for.
- [ ] Maybe: export a whole project (every chapter in one file), and **Print** (the PDF's pages could go
      straight to a printer).
- [ ] Version history (e.g. git under the hood), like Google Docs' revision history.
