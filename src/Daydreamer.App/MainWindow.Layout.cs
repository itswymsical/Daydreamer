using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Daydreamer.App;

// Which parts of the writing view are showing: the side panel can be tucked away (and stays that way
// next time), and focus mode hides both panels and the toolbar so it's just you and the page.
// (Same class as MainWindow.xaml.cs, split by topic.)
public partial class MainWindow
{
    private FoldingPanel _projectPanel = null!;
    private FoldingPanel _sidePanel = null!;
    private bool _focusMode;

    private void SetUpLayout()
    {
        _projectPanel = new FoldingPanel(LeftColumn, LeftSplitterColumn, ProjectPanel, LeftSplitter);
        _sidePanel = new FoldingPanel(RightColumn, RightSplitterColumn, FormattingPanel, RightSplitter);
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        _projectPanel.Show(!_focusMode);
        _sidePanel.Show(!_focusMode && !_settings.SidePanelHidden);
        Toolbar.Visibility = _focusMode ? Visibility.Collapsed : Visibility.Visible;

        FocusButton.IsChecked = _focusMode;
        FocusButton.ToolTip = _focusMode
            ? "Leave focus mode (F11 or Esc)"
            : "Focus mode (F11): hide the panels and the toolbar, so it's just you and the page";
        SidePanelIcon.Data = (System.Windows.Media.Geometry)FindResource(_settings.SidePanelHidden ? "IconSidePanelHidden" : "IconSidePanel");
        SidePanelButton.ToolTip = _settings.SidePanelHidden ? "Show the side panel (Ctrl+\\)" : "Hide the side panel (Ctrl+\\)";
    }

    // ── The side panel ──────────────────────────────────────────────────────

    private void SidePanelToggle_Click(object sender, RoutedEventArgs e) => ToggleSidePanel();

    private void ToggleSidePanel()
    {
        if (_focusMode)
        {
            SetFocusMode(false); // in focus mode the panel is hidden anyway: bring it back
            if (!_settings.SidePanelHidden) return;
        }
        _settings.SidePanelHidden = !_settings.SidePanelHidden;
        SaveSettings();
        ApplyLayout();
    }

    /// <summary>Opens one of the side panel's tabs, bringing the panel back first if it's hidden.</summary>
    private void ShowSidePanelTab(TabItem tab)
    {
        if (_focusMode) SetFocusMode(false);
        if (_settings.SidePanelHidden)
        {
            _settings.SidePanelHidden = false;
            SaveSettings();
            ApplyLayout();
        }
        FormattingTabs.SelectedItem = tab;
    }

    // ── Focus mode ──────────────────────────────────────────────────────────

    // Checked/Unchecked rather than Click, so it also works for screen readers and other accessibility tools.
    private void FocusButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_projectPanel != null) SetFocusMode(FocusButton.IsChecked == true);
    }

    /// <summary>Focus mode lasts until you leave it (or go to the Home page); it isn't remembered between sessions.</summary>
    private void SetFocusMode(bool on)
    {
        if (on && IsHomeShown) on = false; // it's for writing
        if (_focusMode == on)
        {
            ApplyLayout(); // puts the button back if a click on it was refused
            return;
        }
        _focusMode = on;
        ApplyLayout();

        if (on)
        {
            ColorPopup.IsOpen = false; // it belongs to the toolbar, which has gone
            ShowInfo("Focus mode. Press Esc or F11 to bring everything back.", "Leave",
                () => SetFocusMode(false), TimeSpan.FromSeconds(6));
            if (_document != null) Editor.Focus();
        }
        else
        {
            HideInfo();
        }
    }

    /// <summary>Esc leaves focus mode, unless something else wanted it first (closing the find bar, say).</summary>
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _focusMode)
        {
            SetFocusMode(false);
            e.Handled = true;
        }
    }

    /// <summary>
    /// A panel in its own grid column, next to a splitter. It can be tucked away and brought back at the
    /// width it had (including any width you dragged it to).
    /// </summary>
    private sealed class FoldingPanel(ColumnDefinition column, ColumnDefinition splitterColumn, UIElement panel, UIElement splitter)
    {
        private readonly double _minWidth = column.MinWidth;
        private readonly double _splitterWidth = splitterColumn.Width.Value;
        private GridLength _width = column.Width;

        public void Show(bool show)
        {
            if (show == (panel.Visibility == Visibility.Visible)) return;
            if (!show) _width = column.Width;

            column.MinWidth = show ? _minWidth : 0;
            column.Width = show ? _width : new GridLength(0);
            splitterColumn.Width = new GridLength(show ? _splitterWidth : 0);
            panel.Visibility = splitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
