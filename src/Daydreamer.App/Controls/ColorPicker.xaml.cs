using System.Windows;
using System.Windows.Controls;
using Daydreamer.App.Editor;

namespace Daydreamer.App.Controls;

/// <summary>
/// Pick an optional color from the swatches or type a hex code. Empty means "default" — the theme's gray.
/// Bind <see cref="SelectedColor"/> to a string property such as <c>TextStyle.Color</c>.
/// </summary>
public partial class ColorPicker : UserControl
{
    public static readonly string[] Palette =
    [
        "#E03131", "#E8590C", "#F08C00", "#66A80F", "#2F9E44", "#0C8599",
        "#1971C2", "#3B5BDB", "#7048E8", "#9C36B5", "#C2255C", "#8D6E63",
    ];

    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(
        nameof(SelectedColor), typeof(string), typeof(ColorPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedColorChanged));

    private bool _syncing;

    public ColorPicker()
    {
        InitializeComponent();
        Swatches.ItemsSource = Palette;
        UpdateDisplay();
    }

    /// <summary>Raised when a swatch (or Default) is clicked, so a pop-up can close itself.</summary>
    public event EventHandler? Picked;

    public string? SelectedColor
    {
        get => (string?)GetValue(SelectedColorProperty);
        set => SetValue(SelectedColorProperty, value);
    }

    private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ColorPicker)d).UpdateDisplay();

    private void UpdateDisplay()
    {
        if (!_syncing)
        {
            _syncing = true;
            HexBox.Text = SelectedColor ?? "";
            _syncing = false;
        }
        DefaultDot.Visibility = SelectedColor is null ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = HexBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = HexBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_syncing) return;

        string text = HexBox.Text.Trim();
        // Only apply colors WPF can actually read, so half-typed codes don't flicker the editor.
        if (text.Length == 0 || WpfStyles.TryParseColor(text) != null)
        {
            _syncing = true;
            SelectedColor = text.Length == 0 ? null : text;
            _syncing = false;
            DefaultDot.Visibility = SelectedColor is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = (string)((Button)sender).Tag;
        Picked?.Invoke(this, EventArgs.Empty);
    }

    private void Default_Click(object sender, RoutedEventArgs e)
    {
        SelectedColor = null;
        Picked?.Invoke(this, EventArgs.Empty);
    }
}
