# Daydreamer to-do

Features to add, roughly in the order they were asked for. Each note says how it could fit
Daydreamer's way of working: plain text underneath, with formatting applied by rules.

## Formatting

- [x] **Line spacing.** Done. Single / 1.15 / 1.5 / Double / custom, per kind of line (toolbar or side panel).
- [ ] **Alignment: left, center, right, justify.** A per-style setting like line spacing, so you could
      center every CAPTION, or justify all descriptions. Needs a way to align individual lines in the
      editor (AvalonEdit only lays text out left-aligned, so this one needs some research).
- [ ] **Clear formatting button.** Reset the style under the cursor (or a character) back to
      "inherit", and/or strip `**` / `*` markers from the selected text.
- [ ] **Increase / decrease indent.** A per-style indent (e.g. indent all dialogue), plus Tab /
      Shift+Tab to indent single lines.

## Lists

- [ ] **Bulleted list:** lines starting with `- ` or `* `.
- [ ] **Numbered list:** lines starting with `1. `, renumbered automatically.
- [ ] **Checklist:** lines starting with `[ ] ` / `[x] `, with a clickable box.

## Links and structure

- [ ] **Hyperlinks:** recognise web addresses (and maybe `[text](link)`), Ctrl+click to open.
- [ ] **Horizontal lines:** a line of `---` drawn as a divider.
- [ ] **Page breaks:** an explicit break for prose and printing (scripts already have `PAGE` markers).
- [ ] **Bookmarks:** jump to headings, pages, or marked spots from a sidebar outline.

## Pages and view

- [ ] **Page numbers:** shown when printing/exporting (and maybe in the margin while writing).
- [ ] **Full screen mode:** F11 hides the sidebars and toolbar so it's just you and the page.

## Bigger ideas from earlier

- [ ] Export to PDF / Word, using the same formatting rules so exports match the screen.
- [ ] Version history (e.g. git under the hood), like Google Docs' revision history.
