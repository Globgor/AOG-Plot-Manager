using Avalonia;

namespace PlotManager.UI;

static class Program
{
    // Avalonia configuration, do not edit
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseSkia();
}
