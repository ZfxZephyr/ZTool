using Avalonia;
using System;
using ZTool.Services;

namespace ZTool;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        var instance = SingleInstance.Claim();
        if (instance is null)
        {
            // Already running. A second copy would only steal a tray icon and
            // lose the hotkey race — knock on the one that owns it instead.
            SingleInstance.RequestSummon();
            return;
        }

        App.Instance = instance;

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            instance.Dispose();
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
