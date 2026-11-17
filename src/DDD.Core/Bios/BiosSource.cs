using DDD.Core.Oem;
using DDD.Core.Sources;

namespace DDD.Core.Bios;

/// <summary>
/// The BIOS/UEFI firmware. Dell, HP and Lenovo publish machine-readable catalogs; for everything
/// else (MSI, ASUS, Gigabyte, ASRock boards ...) DDD links the board's support page instead.
/// </summary>
public sealed class BiosSource(OemCatalogs catalogs) : IUpdateSource
{
    public const string SourceName = "BIOS";

    public string Name => SourceName;

    public async Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct)
    {
        var system = context.System;
        string maker = Vendors.ShortName(system.Manufacturer);
        context.Report(Name, $"Checking the {maker} BIOS catalog…");
        Finding? finding = maker switch
        {
            "Lenovo" => await LenovoBios.CheckAsync(catalogs, ct).ConfigureAwait(false),
            "Dell" => await DellBios.CheckAsync(catalogs, ct).ConfigureAwait(false),
            "HP" => await HpBios.CheckAsync(catalogs, ct).ConfigureAwait(false),
            _ => null,
        };
        return [finding ?? BoardVendorPages.Create(system)];
    }

    /// <summary>Builds the BIOS line; <paramref name="newer"/> decides between "update available" and "up to date".</summary>
    internal static Finding Make(SystemInfo system, string source, bool newer, string installed, string available, DateTime? released,
        string title, string notes, string? download, string? info, string? releaseNotes, long? size, string severity, string? sha256 = null) => new()
    {
        Kind = UpdateKind.Bios,
        Status = newer ? FindingStatus.UpdateAvailable : FindingStatus.UpToDate,
        Source = source,
        Device = $"BIOS – {system.DisplayName}",
        Category = "BIOS",
        InstalledVersion = installed,
        InstalledDate = system.BiosDate,
        AvailableVersion = available,
        AvailableDate = released,
        Title = title,
        Provider = Vendors.ShortName(system.Manufacturer),
        Notes = notes,
        DownloadUrl = download,
        InfoUrl = info,
        ReleaseNotesUrl = releaseNotes,
        Size = size,
        Severity = severity,
        Sha256 = sha256,
    };

    /// <summary>
    /// Is <paramref name="available"/> newer than <paramref name="installed"/>? Numeric when both are
    /// dotted numbers, otherwise by release date, otherwise "different" counts as newer.
    /// </summary>
    internal static bool IsNewer(string installed, string available, DateTime? installedDate, DateTime? availableDate)
    {
        var a = DriverVersion.ParseOrNull(available);
        var i = DriverVersion.ParseOrNull(installed);
        if (a != null && i != null)
            return a > i;
        if (installedDate != null && availableDate != null)
            return availableDate.Value.Date > installedDate.Value.Date;
        return !string.Equals(installed.Trim(), available.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsNotFound(HttpRequestException ex) =>
        ex.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden;
}
