using System;
using System.IO;
using Avalonia;

namespace Sedhep.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        ApplyDataDirectoryArgument(args);

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// <c>--data-dir &lt;folder&gt;</c> keeps the cookbook somewhere else, which is how a portable
    /// install is set up and how the app is pointed at a folder it is allowed to write to.
    /// </summary>
    private static void ApplyDataDirectoryArgument(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(args[i], "-d", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Environment.SetEnvironmentVariable("SEDHEP_DATA_DIR", Path.GetFullPath(args[i + 1]));
            return;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
