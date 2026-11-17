using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace DDD;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static string? _themeMode;
    private static bool _watchingSystem;

    /// <summary>"Light", "Dark", or anything else to follow Windows (live, when the Windows theme changes).</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Current == null)
            return;
        _themeMode = theme;
        var platform = Current.PlatformSettings;
        if (platform != null && !_watchingSystem)
        {
            _watchingSystem = true;
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_themeMode is not ("Light" or "Dark"))
                    ApplyTheme(_themeMode);
            });
        }
        // resolved explicitly: going back from Light/Dark to ThemeVariant.Default does not re-read the OS theme
        Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => platform?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settings = Settings.Load();
            ApplyTheme(settings.Theme);
            string[] args = desktop.Args ?? [];
            desktop.MainWindow = new MainWindow(settings,
                scanOnOpen: args.Contains("--scan", StringComparer.OrdinalIgnoreCase),
                demo: args.Contains("--demo", StringComparer.OrdinalIgnoreCase),
                aboutOnOpen: args.Contains("--about", StringComparer.OrdinalIgnoreCase));
        }
        base.OnFrameworkInitializationCompleted();
    }
}
