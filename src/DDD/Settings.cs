using System.Text.Json;

namespace DDD;

/// <summary>Remembered choices, in settings.json (%APPDATA%\DDD, or Data\ next to the program when portable).</summary>
public sealed class Settings
{
    public bool WindowsUpdate { get; set; } = true;
    public bool Catalog { get; set; } = true;
    public bool Nvidia { get; set; } = true;
    public bool Bios { get; set; } = true;
    public bool OemDrivers { get; set; } = true;
    public bool PreferMaker { get; set; } = true;
    public bool IncludeMicrosoftDrivers { get; set; }
    public bool NvidiaStudio { get; set; }
    public bool ShowUpToDate { get; set; }

    /// <summary>"System", "Dark" or "Light".</summary>
    public string Theme { get; set; } = "Dark";

    public double? Width { get; set; }
    public double? Height { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public bool Maximized { get; set; }
    public double? DetailWidth { get; set; }

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string FilePath { get; } = Path.Combine(DDD.Core.AppPaths.SettingsFolder, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Options) ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // a broken settings file just means defaults
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // not worth bothering the user about
        }
    }
}
