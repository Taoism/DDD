using System.Globalization;
using System.Management;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DDD.Core;

/// <summary>Reads the PC's model, BIOS and installed drivers from WMI.</summary>
[SupportedOSPlatform("windows")]
public static class SystemInventory
{
    public static SystemInfo GetSystemInfo()
    {
        var cs = First("SELECT Manufacturer, Model, SystemSKUNumber, PCSystemType FROM Win32_ComputerSystem");
        var board = First("SELECT Manufacturer, Product FROM Win32_BaseBoard");
        var bios = First("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
        var os = First("SELECT Caption, BuildNumber FROM Win32_OperatingSystem");

        int revision = 0;
        using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            if (key?.GetValue("UBR") is int ubr)
                revision = ubr;
        }

        return new SystemInfo
        {
            Manufacturer = Str(cs, "Manufacturer"),
            Model = Str(cs, "Model"),
            Sku = Str(cs, "SystemSKUNumber"),
            IsLaptop = cs?["PCSystemType"] is ushort type && type == 2,
            BoardManufacturer = Str(board, "Manufacturer"),
            BoardProduct = Str(board, "Product"),
            BiosVendor = Str(bios, "Manufacturer"),
            BiosVersion = Str(bios, "SMBIOSBIOSVersion"),
            BiosDate = CimDate(Str(bios, "ReleaseDate")),
            OsName = Str(os, "Caption"),
            OsBuild = int.TryParse(Str(os, "BuildNumber"), NumberStyles.None, CultureInfo.InvariantCulture, out int build) ? build : Environment.OSVersion.Version.Build,
            OsRevision = revision,
        };
    }

    /// <summary>Present devices that have a driver, with their hardware IDs.</summary>
    public static IReadOnlyList<DeviceInfo> GetDevices()
    {
        var entities = new Dictionary<string, ManagementBaseObject>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, HardwareID, CompatibleID, ConfigManagerErrorCode FROM Win32_PnPEntity"))
        {
            foreach (var item in searcher.Get())
            {
                if (item["DeviceID"] is string id)
                    entities[id] = item;
            }
        }

        var devices = new List<DeviceInfo>();
        using (var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, InfName, HardWareID FROM Win32_PnPSignedDriver"))
        {
            foreach (var driver in searcher.Get())
            {
                string id = Str(driver, "DeviceID");
                // only devices that are plugged in; signed-driver records also exist for old, absent hardware
                if (id.Length == 0 || !entities.TryGetValue(id, out var entity))
                    continue;
                string version = Str(driver, "DriverVersion");
                if (version.Length == 0)
                    continue;

                string[] hardwareIds = entity["HardwareID"] as string[] ?? [];
                if (hardwareIds.Length == 0 && Str(driver, "HardWareID") is { Length: > 0 } single)
                    hardwareIds = [single];

                devices.Add(new DeviceInfo
                {
                    InstanceId = id,
                    Name = FirstNonEmpty(Str(driver, "DeviceName"), Str(entity, "Name"), id),
                    DeviceClass = Str(driver, "DeviceClass"),
                    Manufacturer = Str(driver, "Manufacturer"),
                    Provider = Str(driver, "DriverProviderName"),
                    VersionText = version,
                    DriverDate = CimDate(Str(driver, "DriverDate")),
                    InfName = Str(driver, "InfName"),
                    HardwareIds = hardwareIds,
                    CompatibleIds = entity["CompatibleID"] as string[] ?? [],
                    HasProblem = entity["ConfigManagerErrorCode"] is uint code && code != 0,
                });
            }
        }
        return devices
            .OrderBy(d => d.DeviceClass, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ManagementBaseObject? First(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        return searcher.Get().Cast<ManagementBaseObject>().FirstOrDefault();
    }

    private static string Str(ManagementBaseObject? obj, string property)
    {
        try
        {
            return obj?[property]?.ToString()?.Trim() ?? "";
        }
        catch (ManagementException)
        {
            return "";
        }
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => v.Length > 0) ?? "";

    /// <summary>
    /// "20260617000000.******+000" → 2026-06-17. Only the date part is used: driver dates are
    /// midnight UTC and converting them to local time would show the day before in the Americas.
    /// </summary>
    internal static DateTime? CimDate(string? value)
    {
        if (value is { Length: >= 8 } && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        return null;
    }
}
