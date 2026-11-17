namespace DDD.Core;

/// <summary>
/// Where DDD keeps its settings and cache. Installed: %APPDATA%\DDD and %LOCALAPPDATA%\DDD.
/// Portable (a file named portable.txt next to the program): a Data folder next to the program,
/// so nothing is written outside it.
/// </summary>
public static class AppPaths
{
    public const string PortableMarker = "portable.txt";

    public static bool IsPortable { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, PortableMarker));

    /// <summary>Settings folder.</summary>
    public static string SettingsFolder { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Data")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DDD");

    /// <summary>Cache folder (catalog search pages).</summary>
    public static string CacheFolder { get; } = IsPortable
        ? Path.Combine(AppContext.BaseDirectory, "Data", "catalog-cache")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DDD", "catalog-cache");
}
