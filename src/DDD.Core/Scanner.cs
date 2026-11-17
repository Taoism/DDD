using System.Runtime.Versioning;
using DDD.Core.Bios;
using DDD.Core.Oem;
using DDD.Core.Sources;

namespace DDD.Core;

/// <summary>Runs a full scan: inventory, then every enabled source in parallel, then merges the answers.</summary>
[SupportedOSPlatform("windows")]
public static class Scanner
{
    public static async Task<ScanResult> ScanAsync(ScanOptions options, IProgress<ScanProgress>? progress, CancellationToken ct)
    {
        progress?.Report(new ScanProgress("Inventory", "Reading this PC's devices and drivers…"));
        var system = await Task.Run(SystemInventory.GetSystemInfo, ct).ConfigureAwait(false);
        var devices = await Task.Run(SystemInventory.GetDevices, ct).ConfigureAwait(false);
        progress?.Report(new ScanProgress("Inventory", $"{devices.Count} devices with drivers"));

        var context = new ScanContext(system, devices, options, progress);
        var catalog = new CatalogClient();
        var sources = new List<IUpdateSource>();
        if (options.WindowsUpdate)
            sources.Add(new WindowsUpdateSource());
        if (options.Catalog)
            sources.Add(new CatalogSource(catalog));
        if (options.Nvidia)
            sources.Add(new NvidiaSource());
        var oem = new OemCatalogs(system);
        if (options.OemDrivers)
            sources.Add(new OemDriverSource(oem));
        if (options.Bios)
            sources.Add(new BiosSource(oem));

        var messages = new List<SourceMessage>();
        var runs = sources.Select(async source =>
        {
            try
            {
                var found = await source.CheckAsync(context, ct).ConfigureAwait(false);
                if (source is CatalogSource { FailedLookups: > 0 } cs)
                {
                    lock (messages)
                        messages.Add(new SourceMessage(source.Name, $"{cs.FailedLookups} device lookup(s) failed (network or catalog error).", false));
                }
                return found;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                lock (messages)
                    messages.Add(new SourceMessage(source.Name, ex.Message, true));
                return [];
            }
        }).ToList();
        var results = await Task.WhenAll(runs).ConfigureAwait(false);

        return new ScanResult(system, devices, Merge(results.SelectMany(r => r), options.PreferMaker), messages, DateTime.Now);
    }

    /// <summary>
    /// One line per device. The same driver found by several sources becomes one finding listing them
    /// all. When sources offer different drivers, <paramref name="preferMaker"/> picks by who made it:
    /// the PC maker (Dell, HP, Lenovo), then the chip maker (NVIDIA), then the vendor's driver in the
    /// Update Catalog, then Windows Update; otherwise the highest version wins. The other offers are
    /// listed in the chosen finding's notes. "Up to date" answers are dropped when there is an update.
    /// </summary>
    public static IReadOnlyList<Finding> Merge(IEnumerable<Finding> findings, bool preferMaker = true)
    {
        var merged = new List<Finding>();
        foreach (var device in findings.GroupBy(Key, StringComparer.OrdinalIgnoreCase))
        {
            var updates = device.Where(f => f.Status == FindingStatus.UpdateAvailable)
                .GroupBy(f => Norm(f.AvailableVersion), StringComparer.OrdinalIgnoreCase)
                .Select(Combine)
                .ToList();
            if (updates.Count == 0)
            {
                foreach (var status in device.GroupBy(f => f.Status))
                    merged.Add(status.OrderBy(Rank).ThenByDescending(Richness).First());
                continue;
            }

            var chosen = preferMaker
                ? updates.OrderBy(Rank).ThenByDescending(f => DriverVersion.ParseOrNull(f.AvailableVersion)).First()
                : updates.OrderByDescending(f => DriverVersion.ParseOrNull(f.AvailableVersion)).ThenBy(Rank).First();
            var notes = new System.Text.StringBuilder(chosen.Notes);
            var others = updates.Where(f => !ReferenceEquals(f, chosen)).ToList();
            if (others.Count > 0)
            {
                notes.Append("\n\nAlso offered:");
                var chosenVersion = DriverVersion.ParseOrNull(chosen.AvailableVersion);
                foreach (var o in others)
                {
                    bool newer = DriverVersion.ParseOrNull(o.AvailableVersion) > chosenVersion;
                    notes.Append("\n• ").Append(o.Source).Append(": ").Append(o.AvailableVersion);
                    if (o.AvailableDate is { } d)
                        notes.Append(" (").Append(d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)).Append(')');
                    if (newer && preferMaker)
                        notes.Append(" – newer, but not from the device's maker");
                }
            }
            var makerCurrent = device.FirstOrDefault(f => f.Status == FindingStatus.UpToDate && Rank(f) == 0);
            if (makerCurrent != null && Rank(chosen) > 0)
            {
                notes.Append("\n\n").Append(makerCurrent.Source).Append("'s own newest driver for this device (")
                    .Append(makerCurrent.AvailableVersion).Append(") is already installed; this update is from ").Append(chosen.Source).Append('.');
            }
            merged.Add(chosen with
            {
                Notes = notes.ToString(),
                Severity = makerCurrent != null && Rank(chosen) > 0 && preferMaker ? "Optional" : chosen.Severity,
            });
        }
        return merged
            .OrderBy(f => f.Status)
            .ThenBy(f => f.Kind == UpdateKind.Bios ? 0 : 1)
            .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Device, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One finding for the same driver from several sources: the most informative one, naming every source.</summary>
    private static Finding Combine(IEnumerable<Finding> same)
    {
        var group = same.OrderBy(Rank).ThenByDescending(Richness).ToList();
        var best = group[0];
        if (group.Count == 1)
            return best;
        var richest = group.OrderByDescending(Richness).First();
        return best with
        {
            Source = string.Join(" + ", group.Select(f => f.Source).Distinct(StringComparer.OrdinalIgnoreCase)),
            Notes = richest.Notes.Length > best.Notes.Length ? richest.Notes : best.Notes,
            DownloadUrl = best.DownloadUrl ?? group.Select(f => f.DownloadUrl).FirstOrDefault(u => u != null),
            ReleaseNotesUrl = best.ReleaseNotesUrl ?? group.Select(f => f.ReleaseNotesUrl).FirstOrDefault(u => u != null),
            InfoUrl = best.InfoUrl ?? group.Select(f => f.InfoUrl).FirstOrDefault(u => u != null),
        };
    }

    /// <summary>0 = PC maker, 1 = chip maker, 2 = vendor driver from the Update Catalog, 3 = Windows Update.</summary>
    internal static int Rank(Finding f) => f.Source.Split(" + ").Min(source => source switch
    {
        _ when OemDriverSource.MakerNames.Contains(source, StringComparer.OrdinalIgnoreCase) => 0,
        NvidiaSource.SourceName => 1,
        CatalogSource.SourceName => 2,
        WindowsUpdateSource.SourceName => 3,
        _ => 1,
    });

    private static string Key(Finding f) => f.DeviceInstanceId ?? f.Device;

    /// <summary>"24.80.0.2.0" and "24.80.0.2" are the same version.</summary>
    private static string Norm(string version)
    {
        var v = DriverVersion.ParseOrNull(version);
        if (v == null)
            return version.Trim();
        int last = v.Parts.Count - 1;
        while (last > 0 && v.Parts[last] == 0)
            last--;
        return string.Join('.', v.Parts.Take(last + 1));
    }

    private static int Richness(Finding f) =>
        f.Notes.Length + (f.DownloadUrl != null ? 500 : 0) + (f.ReleaseNotesUrl != null ? 200 : 0) + (f.InstalledVersion.Length > 0 ? 100 : 0);
}
