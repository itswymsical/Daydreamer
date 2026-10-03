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
- [ ] **Full screen mode:** F11 hides the sidebars and toolbar so it's just you and the page.

## Importing

- [x] Headings, lists, bold/italic, alignment, fonts, sizes and spacing from .docx and pasted HTML.
- [x] Fonts packed inside a .docx are kept with the project.
- [x] Speakers suggested from scripts that don't use colons.
- [ ] Heading sizes and fonts (only the body text's look is imported so far).

## Bigger ideas from earlier

- [ ] Export to PDF / Word, using the same formatting rules so exports match the screen.
- [ ] Version history (e.g. git under the hood), like Google Docs' revision history.
