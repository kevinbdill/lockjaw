using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Lockjaw.App;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            // Support "open with Lockjaw" / double-clicking a .lockjaw file
            if (desktop.Args is { Length: > 0 } args && System.IO.File.Exists(args[0]))
            {
                ((MainWindow)desktop.MainWindow).LoadInitialPaths(args);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
