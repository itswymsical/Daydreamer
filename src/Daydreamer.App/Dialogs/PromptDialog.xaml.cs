using System.Windows;
using Daydreamer.App.Shell;
using Daydreamer.App.Theming;

namespace Daydreamer.App.Dialogs;

/// <summary>
/// Small themed dialogs: ask for a name, confirm something, or report a problem.
/// (WPF's built-in MessageBox can't follow dark mode, and WPF has no input box at all.)
/// </summary>
public partial class PromptDialog : Window
{
    private PromptDialog(string title, string message, string okText)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        SourceInitialized += (_, _) => WindowEffects.UseDarkFrame(this, ThemeManager.IsDark);
    }

    /// <summary>Asks for some text. Returns it trimmed, or null if cancelled or left empty.</summary>
    public static string? Ask(Window owner, string title, string message, string initialText = "", string okText = "OK")
    {
        var dialog = new PromptDialog(title, message, okText) { Owner = owner };
        dialog.InputBox.Text = initialText;
        dialog.Loaded += (_, _) =>
        {
            dialog.InputBox.Focus();
            dialog.InputBox.SelectAll();
        };
        if (dialog.ShowDialog() != true) return null;
        string text = dialog.InputBox.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>Asks "are you sure?". Returns true if the person clicked the OK button.</summary>
    public static bool Confirm(Window owner, string title, string message, string okText)
    {
        var dialog = new PromptDialog(title, message, okText) { Owner = owner };
        dialog.InputBox.Visibility = Visibility.Collapsed;
        dialog.Loaded += (_, _) => dialog.OkButton.Focus();
        return dialog.ShowDialog() == true;
    }

    /// <summary>Tells the person something went wrong.</summary>
    public static void Inform(Window owner, string title, string message)
    {
        var dialog = new PromptDialog(title, message, "OK") { Owner = owner };
        dialog.InputBox.Visibility = Visibility.Collapsed;
        dialog.CancelButton.Visibility = Visibility.Collapsed;
        dialog.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
