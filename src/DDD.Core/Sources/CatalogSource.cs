using System.Text;
using System.Text.RegularExpressions;

namespace DDD.Core.Sources;

/// <summary>
/// Looks every device up in the Microsoft Update Catalog by hardware ID. The catalog often has
/// newer vendor drivers than Windows Update offers automatically.
/// </summary>
public sealed partial class CatalogSource(CatalogClient catalog) : IUpdateSource
{
    public const string SourceName = "Update Catalog";

    // buses whose IDs the catalog indexes; software and root-enumerated devices have nothing there
    private static readonly string[] Buses = ["PCI\\", "USB\\", "HDAUDIO\\", "INTELAUDIO\\", "ACPI\\", "HID\\", "BTHENUM\\", "SD\\", "DISPLAY\\", "MONITOR\\"];

    public string Name => SourceName;

    public async Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct)
    {
        _failed = 0;
        catalog.OnThrottled = message => context.Report(Name, message);
        // identical devices (USB ports, root ports with the same ID ...) are looked up once
        var groups = context.Devices
            .Where(d => IsCandidate(d, context.Options))
            .GroupBy(d => (Query(d).ToUpperInvariant(), d.VersionText, Vendors.Key(d.Provider)))
            .ToList();

        var findings = new List<Finding>();
        int done = 0;
        using var gate = new SemaphoreSlim(Math.Max(1, context.Options.Concurrency));
        var tasks = groups.Select(async group =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var device = group.First();
                var finding = await CheckDeviceAsync(device, context.System, ct).ConfigureAwait(false);
                if (finding != null)
                {
                    lock (findings)
                        findings.Add(finding);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or RegexMatchTimeoutException
                || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // one device failing (timeout, 404) must not stop the others
                Interlocked.Increment(ref _failed);
            }
            finally
            {
                gate.Release();
                int n = Interlocked.Increment(ref done);
                context.Report(Name, $"Checked {n} of {groups.Count} devices");
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
        if (catalog.GaveUp)
            throw new CatalogThrottledException();
        if (_failed > 0 && _failed == groups.Count)
            throw new HttpRequestException("The Microsoft Update Catalog did not answer.");
        return findings;
    }

    private int _failed;

    /// <summary>Devices whose lookup failed in the last check (network errors, timeouts).</summary>
    public int FailedLookups => _failed;

    private static bool IsCandidate(DeviceInfo d, ScanOptions options)
    {
        if (d.HardwareIds.Count == 0 || d.Version == null)
            return false;
        if (!Buses.Any(b => d.HardwareIds[0].StartsWith(b, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (d.IsMicrosoftDriver && !options.IncludeMicrosoftDrivers)
            return false;
        // GeForce cards are checked against NVIDIA directly; the catalog lags months behind
        return !(options.Nvidia && NvidiaSource.IsNvidiaGpu(d));
    }

    /// <summary>
    /// The search text for a device: the plain vendor/device form ("PCI\VEN_8086&amp;DEV_4680"), which
    /// the catalog also matches inside longer, subsystem-specific IDs; otherwise the most specific ID.
    /// </summary>
    public static string Query(DeviceInfo device)
    {
        foreach (string id in device.HardwareIds.Concat(device.CompatibleIds))
        {
            var m = ShortIdRegex().Match(id);
            if (m.Success)
                return m.Value;
        }
        return device.HardwareIds[0];
    }

    private async Task<Finding?> CheckDeviceAsync(DeviceInfo device, SystemInfo system, CancellationToken ct)
    {
        string usedQuery = Query(device);
        var entries = (await catalog.SearchAsync(usedQuery, ct).ConfigureAwait(false)).Where(e => AppliesTo(e, system)).ToList();
        if (entries.Count == 0)
            return null;

        var installed = device.Version!;
        // prefer drivers from the company whose driver is installed now; versions of different vendors don't compare
        var sameVendor = entries.Where(e => TitleVendorMatches(e.Title, device)).ToList();
        var candidates = (sameVendor.Count > 0 ? sameVendor : entries)
            .Where(e => e.Version != null)
            .OrderByDescending(e => e.Version)
            .ThenByDescending(e => e.LastUpdated)
            .ToList();
        if (candidates.Count == 0)
            return null;

        foreach (var entry in candidates.Where(e => e.Version > installed).Take(3))
        {
            var details = await catalog.GetDetailsAsync(entry.UpdateId, ct).ConfigureAwait(false);
            if (details == null || !ArchitectureFits(details.Architecture))
                continue;
            string provider = details.Provider.Length > 0 ? details.Provider : details.Manufacturer;
            if (!device.IsMicrosoftDriver && provider.Length > 0 && !Vendors.SameCompany(provider, device.Provider))
                continue;

            IReadOnlyList<string> urls = [];
            try
            {
                urls = await CatalogClient.GetDownloadUrlsAsync(entry.UpdateId, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // the details page still lets the user download it
            }

            return new Finding
            {
                Kind = UpdateKind.Driver,
                Status = FindingStatus.UpdateAvailable,
                Source = Name,
                Device = device.Name,
                Category = device.DeviceClass,
                DeviceInstanceId = device.InstanceId,
                InstalledVersion = device.VersionText,
                InstalledDate = device.DriverDate,
                AvailableVersion = entry.Version!.ToString(),
                AvailableDate = details.VersionDate ?? entry.LastUpdated,
                Title = entry.Title,
                Provider = provider,
                Notes = BuildNotes(entry, details, device),
                DownloadUrl = urls.Count > 0 ? urls[0] : null,
                MoreDownloadUrls = urls.Skip(1).ToList(),
                InfoUrl = CatalogClient.DetailsUrl(entry.UpdateId),
                ReleaseNotesUrl = UsefulLink(details.MoreInfoUrl) ?? UsefulLink(details.SupportUrl),
                Size = entry.Size,
            };
        }

        var newest = candidates[0];
        return new Finding
        {
            Kind = UpdateKind.Driver,
            Status = FindingStatus.UpToDate,
            Source = Name,
            Device = device.Name,
            Category = device.DeviceClass,
            DeviceInstanceId = device.InstanceId,
            InstalledVersion = device.VersionText,
            InstalledDate = device.DriverDate,
            AvailableVersion = newest.Version!.ToString(),
            AvailableDate = newest.LastUpdated,
            Title = newest.Title,
            Provider = device.Provider,
            Notes = $"The newest driver in the Microsoft Update Catalog for this device is {newest.Version} ({newest.Title}); the installed {device.VersionText} is the same or newer.",
            InfoUrl = CatalogClient.SearchUrl(usedQuery),
        };
    }

    /// <summary>Rows for this Windows generation; "Windows 10 ... and later" also covers Windows 11.</summary>
    internal static bool AppliesTo(CatalogEntry entry, SystemInfo system)
    {
        if (!entry.Classification.StartsWith("Drivers", StringComparison.OrdinalIgnoreCase) && entry.Classification.Length > 0)
            return false;
        if (entry.Title.Contains("ARM64", StringComparison.OrdinalIgnoreCase))
            return false;
        string p = entry.Products;
        if (p.Length == 0)
            return true;
        if (system.IsWindows11)
            return p.Contains("Windows 11", StringComparison.OrdinalIgnoreCase)
                || (p.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) && p.Contains("and later", StringComparison.OrdinalIgnoreCase));
        return p.Contains("Windows 10", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ArchitectureFits(string architecture)
    {
        if (architecture.Length == 0)
            return true;
        return Environment.Is64BitOperatingSystem
            ? architecture.Contains("AMD64", StringComparison.OrdinalIgnoreCase) || architecture.Contains("X64", StringComparison.OrdinalIgnoreCase)
            : architecture.Contains("X86", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Many vendors put Microsoft's generic hardware-submission page as "more information"; that is no release note.</summary>
    public static string? UsefulLink(string? url) =>
        url == null || url.Contains("learn.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("go.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || url.Contains("windows-hardware", StringComparison.OrdinalIgnoreCase)
            || url.Contains("support.microsoft.com", StringComparison.OrdinalIgnoreCase)
            ? null : url;

    private static bool TitleVendorMatches(string title, DeviceInfo device)
    {
        string key = Vendors.Key(device.Provider);
        return key.Length > 0 && title.StartsWith(key, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildNotes(CatalogEntry entry, CatalogDetails details, DeviceInfo device)
    {
        var sb = new StringBuilder();
        if (details.Description.Length > 0)
            sb.AppendLine(details.Description).AppendLine();
        sb.AppendLine($"Newer driver from {Vendors.ShortName(details.Provider)}: {entry.Version} (installed: {device.VersionText}).");
        if (details.DriverModel.Length > 0)
            sb.AppendLine("Made for: " + details.DriverModel.Replace(",", ", ", StringComparison.Ordinal));
        if (entry.Products.Length > 0)
            sb.AppendLine("Windows versions: " + entry.Products);
        sb.AppendLine();
        sb.Append("Microsoft's catalog only lists what the vendor submitted; for a detailed change list see the vendor's release notes.");
        if (device.IsMicrosoftDriver)
            sb.AppendLine().AppendLine().Append("Windows uses its own built-in driver for this device now; this is the vendor's driver.");
        return sb.ToString();
    }

    [GeneratedRegex(@"^(PCI\\VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4}|USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}|HDAUDIO\\FUNC_[0-9A-F]{2}&VEN_[0-9A-F]{4}&DEV_[0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ShortIdRegex();
}
