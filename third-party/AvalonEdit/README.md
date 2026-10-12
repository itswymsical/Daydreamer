# AvalonEdit (included as source)

This is [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) 6.3.1, the WPF text editor
Daydreamer's page is built on. It's MIT-licensed (see [LICENSE](LICENSE)).

It's included as source code instead of a NuGet package because Daydreamer needs paragraph
features the published package doesn't have. Every change is marked with a `Daydreamer:`
comment, so they're easy to find (and to re-apply on a newer AvalonEdit):

- `Rendering/VisualLineTextParagraphProperties.cs`: alignment comes from a field instead of
  always being left.
- `Rendering/TextView.cs`: two new properties, `LineAlignmentProvider` (left / center / right /
  justify per line) and `WrapIndentProvider` (where wrapped rows line up, used for lists), applied
  while each line is laid out.

The project file was simplified to build only for Daydreamer's .NET version, without NuGet
packaging or strong-name signing.
