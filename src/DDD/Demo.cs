using DDD.Core;

namespace DDD;

/// <summary>
/// A made-up PC and scan result for screenshots and trying the interface (DDD.exe --demo):
/// nothing is read from this computer and nothing is downloaded.
/// </summary>
internal static class Demo
{
    public static ScanResult Result()
    {
        var system = new SystemInfo
        {
            Manufacturer = "Example Computers",
            Model = "Example Desktop",
            BoardManufacturer = "Example Boards",
            BoardProduct = "Z790 Gaming",
            BiosVersion = "1.40",
            BiosDate = new DateTime(2026, 3, 12),
            OsName = "Microsoft Windows 11 Pro",
            OsBuild = 26200,
            OsRevision = 7171,
        };

        var devices = new List<DeviceInfo>
        {
            Dev("gpu", "NVIDIA GeForce RTX 4070", "Display", "NVIDIA", "32.0.15.9612", 2026, 8, 20, @"PCI\VEN_10DE&DEV_2786&SUBSYS_00000000&REV_A1", "oem12.inf"),
            Dev("igpu", "Intel(R) UHD Graphics 770", "Display", "Intel Corporation", "32.0.101.7088", 2026, 6, 17, @"PCI\VEN_8086&DEV_4680&SUBSYS_00000000&REV_0C", "oem33.inf"),
            Dev("wifi", "Intel(R) Wi-Fi 6E AX211 160MHz", "Net", "Intel", "23.60.1.2", 2025, 11, 2, @"PCI\VEN_8086&DEV_7A70&SUBSYS_00948086&REV_11", "oem41.inf"),
            Dev("bt", "Intel(R) Wireless Bluetooth(R)", "Bluetooth", "Intel Corporation", "23.60.0.1", 2025, 11, 2, @"USB\VID_8087&PID_0033&REV_0000", "oem44.inf"),
            Dev("lan", "Intel(R) Ethernet Controller I226-V", "Net", "Intel", "2.1.5.12", 2026, 3, 24, @"PCI\VEN_8086&DEV_125C&SUBSYS_00000000&REV_04", "oem20.inf"),
            Dev("audio", "Realtek High Definition Audio", "Media", "Realtek Semiconductor Corp.", "6.0.9600.1", 2025, 9, 9, @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_00000000&REV_1001", "oem51.inf"),
            Dev("mei", "Intel(R) Management Engine Interface #1", "System", "Intel", "2540.8.0.0", 2025, 10, 1, @"PCI\VEN_8086&DEV_7A68&SUBSYS_00000000&REV_11", "oem8.inf"),
            Dev("smbus", "Intel(R) SMBus - 7A23", "System", "INTEL", "10.1.46.5", 1968, 7, 18, @"PCI\VEN_8086&DEV_7A23&SUBSYS_00000000&REV_11", "oem3.inf"),
            Dev("nvme", "Standard NVM Express Controller", "SCSIAdapter", "Microsoft", "10.0.26100.7171", 2006, 6, 21, @"PCI\VEN_144D&DEV_A80C&SUBSYS_00000000&REV_00", "stornvme.inf"),
            Dev("usb", "USB Root Hub (USB 3.0)", "USB", "Microsoft", "10.0.26100.7171", 2006, 6, 21, @"USB\ROOT_HUB30&VID8086&PID7A60&REV0011", "usbhub3.inf"),
            Dev("mouse", "HID-compliant mouse", "Mouse", "Microsoft", "10.0.26100.1", 2006, 6, 21, @"HID\VID_046D&PID_C52B&REV_1211&MI_01&Col01", "msmouse.inf"),
        };

        var findings = new List<Finding>
        {
            new()
            {
                Kind = UpdateKind.Driver, Status = FindingStatus.UpdateAvailable, Source = "NVIDIA",
                Device = "NVIDIA GeForce RTX 4070", Category = "Display", DeviceInstanceId = "gpu",
                InstalledVersion = "596.12", InstalledDate = new DateTime(2026, 8, 20),
                AvailableVersion = "621.24", AvailableDate = new DateTime(2026, 11, 11),
                Title = "GeForce Game Ready Driver 621.24", Provider = "NVIDIA", Severity = "WHQL",
                Size = 1_100_000_000,
                Notes = "Game Ready for the newest releases\n" +
                        "This new Game Ready Driver provides the best experience for the latest games and adds support for new DLSS titles.\n\n" +
                        "Fixed Gaming Bugs\n• Stutter in some DirectX 12 games when frame generation is on\n\n" +
                        "Fixed General Bugs\n• Incorrect colors with some HDR video playback\n• Display may flicker after waking from sleep on high-refresh monitors\n\n" +
                        "Also offered:\n• Windows Update: 32.0.15.9640 (2026-10-02)",
                DownloadUrl = "https://example.com/demo/621.24-desktop-win10-win11-64bit.exe",
                InfoUrl = "https://www.nvidia.com/en-us/drivers/",
            },
            new()
            {
                Kind = UpdateKind.Driver, Status = FindingStatus.UpdateAvailable, Source = "Update Catalog",
                Device = "Intel(R) Wi-Fi 6E AX211 160MHz", Category = "Net", DeviceInstanceId = "wifi",
                InstalledVersion = "23.60.1.2", InstalledDate = new DateTime(2025, 11, 2),
                AvailableVersion = "24.70.0.4", AvailableDate = new DateTime(2026, 7, 31),
                Title = "Intel Corporation Net Driver Update (24.70.0.4)", Provider = "Intel Corporation", Size = 9_800_000,
                DownloadUrl = "https://example.com/demo/wifi_24.70.0.4.cab",
                Notes = "Intel Corporation Net driver update released in July 2026\n\n" +
                        "Newer driver from Intel: 24.70.0.4 (installed: 23.60.1.2).\n" +
                        "Made for: Intel(R) Wi-Fi 6E AX211 160MHz, Intel(R) Wi-Fi 6E AX210 160MHz\n" +
                        "Windows versions: Windows 11 Client, version 24H2 and later, Servicing Drivers",
                InfoUrl = "https://www.catalog.update.microsoft.com/",
            },
            new()
            {
                Kind = UpdateKind.Driver, Status = FindingStatus.UpdateAvailable, Source = "Update Catalog + Windows Update",
                Device = "Intel(R) Wireless Bluetooth(R)", Category = "Bluetooth", DeviceInstanceId = "bt",
                InstalledVersion = "23.60.0.1", InstalledDate = new DateTime(2025, 11, 2),
                AvailableVersion = "24.70.0.4", AvailableDate = new DateTime(2026, 7, 31),
                Title = "Intel Corporation Bluetooth Driver Update (24.70.0.4)", Provider = "Intel Corporation", Size = 3_100_000,
                DownloadUrl = "https://example.com/demo/bluetooth_24.70.0.4.cab",
                Severity = "Optional",
                Notes = "Intel Corporation Bluetooth driver update released in July 2026\n\n" +
                        "Windows Update offers this as an optional driver update (Settings › Windows Update › Advanced options › Optional updates).",
                InfoUrl = "https://www.catalog.update.microsoft.com/",
            },
            new()
            {
                Kind = UpdateKind.Driver, Status = FindingStatus.UpdateAvailable, Source = "Update Catalog",
                Device = "Intel(R) Management Engine Interface #1", Category = "System", DeviceInstanceId = "mei",
                InstalledVersion = "2540.8.0.0", InstalledDate = new DateTime(2025, 10, 1),
                AvailableVersion = "2603.9.4.0", AvailableDate = new DateTime(2026, 1, 12),
                Title = "Intel - System - 2603.9.4.0", Provider = "Intel", Size = 1_400_000,
                DownloadUrl = "https://example.com/demo/mei_2603.9.4.0.cab",
                Notes = "Intel System driver update released in January 2026\n\nNewer driver from Intel: 2603.9.4.0 (installed: 2540.8.0.0).",
                InfoUrl = "https://www.catalog.update.microsoft.com/",
            },
            new()
            {
                Kind = UpdateKind.Bios, Status = FindingStatus.CheckManually, Source = "Example Boards",
                Device = "BIOS – Example Boards Z790 Gaming", Category = "BIOS",
                InstalledVersion = "1.40", InstalledDate = new DateTime(2026, 3, 12),
                Title = "Example Boards Z790 Gaming BIOS", Provider = "Example Boards",
                Notes = "Installed: BIOS 1.40 from 2026-03-12 on Example Boards Z790 Gaming.\n\n" +
                        "This board maker does not publish a BIOS catalog that programs can read, so DDD can't tell whether a newer version exists. " +
                        "Open the support page and compare the newest version there with the installed one.",
                InfoUrl = "https://example.com/",
            },
            Current("lan", "Intel(R) Ethernet Controller I226-V", "Net", "2.1.5.12", "Update Catalog"),
            Current("igpu", "Intel(R) UHD Graphics 770", "Display", "32.0.101.7088", "Update Catalog"),
            Current("audio", "Realtek High Definition Audio", "Media", "6.0.9600.1", "Update Catalog"),
            Current("smbus", "Intel(R) SMBus - 7A23", "System", "10.1.46.5", "Update Catalog"),
        };
        return new ScanResult(system, devices, Scanner.Merge(findings), [], new DateTime(2026, 11, 17, 14, 0, 0));
    }

    /// <summary>Example download states for the Download column (nothing is downloaded).</summary>
    public static void FakeDownloads(ScanResult result, Func<Finding, DDD.Core.Install.DownloadItem> track)
    {
        foreach (var f in result.Findings.Where(f => f.DownloadUrl != null))
        {
            var item = track(f);
            if (f.Source == "NVIDIA")
                item.Set(DDD.Core.Install.DownloadState.Ready, "Installer signed by NVIDIA Corporation.");
            else if (f.Device.Contains("Wi-Fi", StringComparison.Ordinal))
                item.Set(DDD.Core.Install.DownloadState.Downloading, "", 42);
            else if (f.Device.Contains("Bluetooth", StringComparison.Ordinal))
                item.Set(DDD.Core.Install.DownloadState.Installed, "Installed with pnputil.");
        }
    }

    private static DeviceInfo Dev(string id, string name, string cls, string provider, string version, int y, int m, int d, string hwid, string inf) => new()
    {
        InstanceId = id,
        Name = name,
        DeviceClass = cls,
        Provider = provider,
        Manufacturer = provider,
        VersionText = version,
        DriverDate = new DateTime(y, m, d),
        HardwareIds = [hwid],
        InfName = inf,
    };

    private static Finding Current(string id, string name, string cls, string version, string source) => new()
    {
        Kind = UpdateKind.Driver,
        Status = FindingStatus.UpToDate,
        Source = source,
        Device = name,
        Category = cls,
        DeviceInstanceId = id,
        InstalledVersion = version,
        AvailableVersion = version,
        Notes = $"The newest driver for this device is {version}; the installed one is the same.",
    };
}
