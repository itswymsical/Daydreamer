using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Daydreamer.App.Theming;

namespace Daydreamer.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Pick light or dark before the main window appears, so it never flashes the wrong colors.
        // `--theme light|dark` overrides the saved choice (handy for testing).
        int themeArg = Array.IndexOf(e.Args, "--theme");
        string? theme = themeArg >= 0 && themeArg + 1 < e.Args.Length ? e.Args[themeArg + 1] : AppSettings.Load().Theme;
        ThemeManager.Apply(ThemeManager.FromSetting(theme));

        // Never lose work to an unexpected error: show it instead of crashing.
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Daydreamer hit a problem", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

#if DEBUG
        // Developer helper: `Daydreamer.App.exe --screenshot out.png` renders the window to an image and exits.
        int index = Array.IndexOf(e.Args, "--screenshot");
        if (index >= 0 && index + 1 < e.Args.Length)
            CaptureScreenshotAndExit(e.Args[index + 1]);
#endif
    }

#if DEBUG
    private void CaptureScreenshotAndExit(string path)
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var window = MainWindow;
            var dpi = VisualTreeHelper.GetDpi(window);
            var content = (FrameworkElement)window.Content;
            var bitmap = new RenderTargetBitmap(
                (int)(content.ActualWidth * dpi.DpiScaleX), (int)(content.ActualHeight * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(path)) encoder.Save(stream);
            Shutdown();
        };
        timer.Start();
    }
#endif
}
