namespace DDD.Core;

/// <summary>What an update is for.</summary>
public enum UpdateKind
{
    Driver,
    Bios,
}

/// <summary>Result of checking one device or component against one source.</summary>
public enum FindingStatus
{
    /// <summary>A newer version than the installed one was found.</summary>
    UpdateAvailable,

    /// <summary>The source knows this device and has nothing newer.</summary>
    UpToDate,

    /// <summary>The vendor has no feed a tool can read; the finding links to the vendor's page instead.</summary>
    CheckManually,
}

/// <summary>A device with an installed driver, as Windows reports it.</summary>
public sealed class DeviceInfo
{
    public required string InstanceId { get; init; }
    public required string Name { get; init; }
    public string DeviceClass { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string Provider { get; init; } = "";
    public string VersionText { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public string InfName { get; init; } = "";

    /// <summary>Most specific first, as Windows lists them.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    public IReadOnlyList<string> CompatibleIds { get; init; } = [];

    /// <summary>Device Manager shows a problem (yellow bang) for this device.</summary>
    public bool HasProblem { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public DriverVersion? Version => DriverVersion.TryParse(VersionText, out var v) ? v : null;

    public bool IsMicrosoftDriver => Provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="id"/> is one of this device's hardware or compatible IDs.</summary>
    public bool Matches(string id) =>
        HardwareIds.Contains(id, StringComparer.OrdinalIgnoreCase) || CompatibleIds.Contains(id, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => $"{Name} ({VersionText})";
}

/// <summary>The PC itself: maker, model, board, BIOS and Windows build.</summary>
public sealed class SystemInfo
{
    public string Manufacturer { get; init; } = "";
    public string Model { get; init; } = "";
    public string Sku { get; init; } = "";
    public string BoardManufacturer { get; init; } = "";
    public string BoardProduct { get; init; } = "";
    public string BiosVendor { get; init; } = "";
    public string BiosVersion { get; init; } = "";
    public DateTime? BiosDate { get; init; }
    public string OsName { get; init; } = "";
    public int OsBuild { get; init; }
    public int OsRevision { get; init; }
    public bool IsLaptop { get; init; }

    public bool IsWindows11 => OsBuild >= 22000;

    public string OsBuildText => OsRevision > 0 ? $"{OsBuild}.{OsRevision}" : OsBuild.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"Micro-Star International Co., Ltd. MS-7D75" or, for custom builds, the board.</summary>
    public string DisplayName
    {
        get
        {
            bool genericModel = string.IsNullOrWhiteSpace(Model) || Model.Contains("To be filled", StringComparison.OrdinalIgnoreCase)
                || Model.Contains("System Product Name", StringComparison.OrdinalIgnoreCase);
            if (!genericModel && !string.Equals(Model, BoardProduct, StringComparison.OrdinalIgnoreCase)
                && !BoardProduct.Contains(Model, StringComparison.OrdinalIgnoreCase))
            {
                return $"{Vendors.ShortName(Manufacturer)} {Model}".Trim();
            }
            return $"{Vendors.ShortName(BoardManufacturer)} {BoardProduct}".Trim();
        }
    }
}

/// <summary>One line in the results: a device or BIOS with what a source knows about it.</summary>
public sealed record Finding
{
    public required UpdateKind Kind { get; init; }
    public required FindingStatus Status { get; init; }

    /// <summary>Where the information came from ("Windows Update", "Microsoft Update Catalog", "NVIDIA", "Dell" ...).</summary>
    public required string Source { get; init; }

    /// <summary>Device or component name.</summary>
    public required string Device { get; init; }

    public string Category { get; init; } = "";
    public string? DeviceInstanceId { get; init; }
    public string InstalledVersion { get; init; } = "";
    public DateTime? InstalledDate { get; init; }
    public string AvailableVersion { get; init; } = "";
    public DateTime? AvailableDate { get; init; }

    /// <summary>The update's own name, e.g. "Intel Corporation Display Driver Update (32.0.101.7088)".</summary>
    public string Title { get; init; } = "";

    public string Provider { get; init; } = "";

    /// <summary>What the update improves or fixes, as plain text.</summary>
    public string Notes { get; init; } = "";

    /// <summary>Direct download of the package (.exe, .cab ...), when known.</summary>
    public string? DownloadUrl { get; init; }

    public IReadOnlyList<string> MoreDownloadUrls { get; init; } = [];

    /// <summary>The vendor's or catalog's page for this update.</summary>
    public string? InfoUrl { get; init; }

    public string? ReleaseNotesUrl { get; init; }
    public long? Size { get; init; }

    /// <summary>SHA-256 of the download, when the vendor publishes it (Dell, HP, Lenovo).</summary>
    public string? Sha256 { get; init; }

    /// <summary>"Critical", "Recommended", "Optional" when the source says.</summary>
    public string Severity { get; init; } = "";
}

/// <summary>A source that failed or was skipped, with why.</summary>
public sealed record SourceMessage(string Source, string Message, bool IsError);

/// <summary>Everything one scan produced.</summary>
public sealed record ScanResult(
    SystemInfo System,
    IReadOnlyList<DeviceInfo> Devices,
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<SourceMessage> Messages,
    DateTime Finished);

/// <summary>Progress line from a running scan.</summary>
public sealed record ScanProgress(string Source, string Message);
