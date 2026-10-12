using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Daydreamer.App.Shell;
using Daydreamer.App.Theming;

namespace Daydreamer.App;

// The custom window frame ("shell"): our own title bar replaces Windows' one. WindowChrome (in the XAML)
// makes the top strip drag the window and the edges resize it; this file adds the caption buttons
// (including Windows 11 snap layouts), maximizing correctly, and the light/dark and ambient toggles.
public partial class MainWindow
{
    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCLBUTTONDOWN = 0x00A1;
    private const int WM_NCLBUTTONUP = 0x00A2;
    private const int WM_NCMOUSELEAVE = 0x02A2;
    private const int HTMAXBUTTON = 9;
    private const int SM_CXSIZEFRAME = 32;
    private const int SM_CYSIZEFRAME = 33;
    private const int SM_CXPADDEDBORDER = 92;

    private readonly DispatcherTimer _maximizeHoverWatch = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private bool _maximizeHovered;
    private bool _maximizePressed;
    private bool _ambientEnabled;

    [DllImport("user32.dll")]
    private static extern int GetSystemMetricsForDpi(int index, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private void SetUpShell()
    {
        RestoreWindowPlacement();
        SourceInitialized += (_, _) =>
        {
            WindowEffects.UseRoundedCorners(this);
            WindowEffects.UseDarkFrame(this, ThemeManager.IsDark);
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(ShellHook);
        };
        StateChanged += (_, _) => OnWindowStateChanged();

        // Windows doesn't always say when the mouse leaves the maximize button, so double-check.
        MouseMove += (_, _) => SetMaximizeHover(false);
        _maximizeHoverWatch.Tick += (_, _) =>
        {
            if (!GetCursorPos(out var p) || !IsScreenPointOver(MaximizeButton, new Point(p.X, p.Y)))
                SetMaximizeHover(false);
        };

        // Ambient mode is on unless it was turned off, or Windows' "Animation effects" setting is off.
        _ambientEnabled = _settings.Ambient ?? SystemParameters.ClientAreaAnimation;
        UpdateAmbient();
        UpdateShellButtons();
    }

    /// <summary>Ambient mode is a dark-mode effect: it runs when it's switched on, the theme is dark, and the page is on screen.</summary>
    private void UpdateAmbient()
    {
        bool run = _ambientEnabled && ThemeManager.IsDark && WindowState != WindowState.Minimized && !IsHomeShown;
        if (run) AmbientLights.Start();
        else AmbientLights.Stop();
        AmbientButton.Visibility = ThemeManager.IsDark ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Size and position ───────────────────────────────────────────────────

    /// <summary>Reopens where the window was last time, or on first run sizes it to fit this screen.</summary>
    private void RestoreWindowPlacement()
    {
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);

        if (_settings.Window is { } saved && saved.Width >= MinWidth && saved.Height >= MinHeight && IsTitleBarOnScreen(saved))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top, Width, Height) = (saved.Left, saved.Top, saved.Width, saved.Height);
            if (saved.Maximized) WindowState = WindowState.Maximized;
            return;
        }

        // A window taller than the screen would open with its title bar off the top edge.
        Width = Math.Min(Width, work.Width * 0.94);
        Height = Math.Min(Height, work.Height * 0.94);
    }

    private void RememberWindowPlacement()
    {
        var bounds = RestoreBounds; // the normal-size bounds, even while maximized
        if (bounds.IsEmpty) return;
        _settings.Window = new AppSettings.WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = WindowState == WindowState.Maximized,
        };
        SaveSettings();
    }

    /// <summary>True if enough of the saved title bar is visible to grab (screens can be unplugged between sessions).</summary>
    private static bool IsTitleBarOnScreen(AppSettings.WindowPlacement saved)
    {
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = Rect.Intersect(screens, new Rect(saved.Left, saved.Top, saved.Width, 40));
        return !titleBar.IsEmpty && titleBar.Width >= 120 && titleBar.Height >= 20;
    }

    // ── Title bar toggles ───────────────────────────────────────────────────

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        var theme = ThemeManager.IsDark ? AppTheme.Light : AppTheme.Dark;
        _settings.Theme = theme.ToString();
        SaveSettings();
        ThemeManager.Apply(theme); // everything else follows via OnThemeChanged
    }

    private void AmbientToggle_Click(object sender, RoutedEventArgs e)
    {
        _ambientEnabled = !_ambientEnabled;
        _settings.Ambient = _ambientEnabled;
        SaveSettings();
        UpdateAmbient();
        UpdateShellButtons();
    }

    private void UpdateShellButtons()
    {
        bool dark = ThemeManager.IsDark;
        ThemeIcon.Text = dark ? "" : ""; // sun / moon
        ThemeButton.ToolTip = dark ? "Switch to light mode" : "Switch to dark mode";
        AmbientIcon.Text = _ambientEnabled ? "" : ""; // filled / outline star
        AmbientButton.ToolTip = _ambientEnabled
            ? "Ambient mode is on: soft blue lights drifting over the page. Click to turn off."
            : "Ambient mode is off. Click for soft blue lights drifting over the page.";
        UpdateMaximizeVisual();
    }

    /// <summary>The Insert buttons show their labels only on very wide windows; otherwise just icons (with tooltips).</summary>
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var visibility = ActualWidth < 1760 ? Visibility.Collapsed : Visibility.Visible;
        if (!Equals(Resources["ToolbarLabelVisibility"], visibility))
            Resources["ToolbarLabelVisibility"] = visibility;
    }

    // ── Caption buttons ─────────────────────────────────────────────────────

    private void Minimize_Click(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void Maximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void Close_Click(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void ToggleMaximize()
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void OnWindowStateChanged()
    {
        bool maximized = WindowState == WindowState.Maximized;
        WindowRoot.Margin = maximized ? MaximizedInset() : new Thickness(0);
        MaximizeButton.Content = maximized ? "" : ""; // restore / maximize glyph
        AutomationProperties.SetName(MaximizeButton, maximized ? "Restore" : "Maximize");

        UpdateAmbient(); // no point animating lights nobody can see
    }

    /// <summary>
    /// A maximized window is slightly bigger than the screen (by the width of its invisible resize border),
    /// so without this the edges of our title bar would be cut off.
    /// </summary>
    private Thickness MaximizedInset()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        uint dpiValue = (uint)Math.Round(dpi.PixelsPerInchX);
        try
        {
            int padding = GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpiValue);
            double x = (GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpiValue) + padding) / dpi.DpiScaleX;
            double y = (GetSystemMetricsForDpi(SM_CYSIZEFRAME, dpiValue) + padding) / dpi.DpiScaleY;
            return new Thickness(x, y, x, y);
        }
        catch (EntryPointNotFoundException)
        {
            return new Thickness(8); // older Windows: the usual size at 100% scaling
        }
    }

    // ── Windows 11 snap layouts ─────────────────────────────────────────────
    // Hovering a window's maximize button shows the snap layouts flyout, but only if Windows knows where
    // that button is. When Windows asks "what's under the mouse?" (WM_NCHITTEST) over ours, we answer
    // "the maximize button". Windows then sends that spot's mouse messages to us as "non-client" ones,
    // so we draw the hover/pressed look and do the click ourselves.

    private IntPtr ShellHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                if (IsScreenPointOver(MaximizeButton, PointFromLParam(lParam)))
                {
                    SetMaximizeHover(true);
                    handled = true;
                    return HTMAXBUTTON;
                }
                SetMaximizeHover(false);
                break;

            case WM_NCLBUTTONDOWN when (int)wParam == HTMAXBUTTON:
                _maximizePressed = true;
                UpdateMaximizeVisual();
                handled = true;
                break;

            case WM_NCLBUTTONUP when (int)wParam == HTMAXBUTTON:
                if (_maximizePressed) ToggleMaximize();
                _maximizePressed = false;
                UpdateMaximizeVisual();
                handled = true;
                break;

            case WM_NCMOUSELEAVE:
                SetMaximizeHover(false);
                break;
        }
        return IntPtr.Zero;
    }

    private void SetMaximizeHover(bool hovered)
    {
        if (_maximizeHovered == hovered) return;
        _maximizeHovered = hovered;
        if (!hovered) _maximizePressed = false;
        if (hovered) _maximizeHoverWatch.Start();
        else _maximizeHoverWatch.Stop();
        UpdateMaximizeVisual();
    }

    private void UpdateMaximizeVisual()
    {
        if (_maximizePressed) MaximizeButton.Background = (Brush)FindResource("PressedBrush");
        else if (_maximizeHovered) MaximizeButton.Background = (Brush)FindResource("HoverBrush");
        else MaximizeButton.ClearValue(Control.BackgroundProperty);
    }

    private static bool IsScreenPointOver(FrameworkElement element, Point screenPoint)
    {
        if (!element.IsVisible || PresentationSource.FromVisual(element) == null) return false;
        var local = element.PointFromScreen(screenPoint);
        return local.X >= 0 && local.Y >= 0 && local.X < element.ActualWidth && local.Y < element.ActualHeight;
    }

    /// <summary>Mouse messages pack the screen position into one number: x in the low 16 bits, y in the next 16.</summary>
    private static Point PointFromLParam(IntPtr lParam)
    {
        long value = lParam.ToInt64();
        return new Point(unchecked((short)(value & 0xFFFF)), unchecked((short)((value >> 16) & 0xFFFF)));
    }
}
