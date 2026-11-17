using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDD.Core.Sources;

namespace DDD.Core.Oem;

/// <summary>A driver package from the PC maker's catalog and the hardware it is for.</summary>
public sealed record OemPackage
{
    public required string Maker { get; init; }
    public required string Name { get; init; }

    /// <summary>The package's version (Dell vendor version, HP SoftPaq version, Lenovo package version).</summary>
    public required string Version { get; init; }

    public DateTime? Released { get; init; }

    /// <summary>Hardware IDs (or ID prefixes such as "PCI\VEN_8086&amp;DEV_4680") the package installs on.</summary>
    public IReadOnlyList<string> HardwareIds { get; init; } = [];

    /// <summary>Driver version the package installs for a hardware ID, when the catalog says (Lenovo).</summary>
    public IReadOnlyDictionary<string, string> DriverVersions { get; init; } = new Dictionary<string, string>();

    public string Description { get; init; } = "";
    public string? DownloadUrl { get; init; }
    public string? InfoUrl { get; init; }
    public string? ReleaseNotesUrl { get; init; }
    public long? Size { get; init; }
    public string Severity { get; init; } = "";
}

/// <summary>
/// Drivers from the PC maker's own catalog (Dell, HP, Lenovo): tested for this model, often with
/// maker-specific settings that the chip vendor's or Windows Update's generic driver lacks.
/// </summary>
public sealed partial class OemDriverSource(OemCatalogs catalogs) : IUpdateSource
{
    public string Name => catalogs.Maker.Length > 0 ? catalogs.Maker : "PC maker";

    /// <summary>Sources that count as the PC maker (preferred over everything else).</summary>
    public static readonly string[] MakerNames = ["Dell", "HP", "Lenovo"];

    public async Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct)
    {
        if (catalogs.Maker.Length == 0)
            return [];
        context.Report(Name, $"Reading {catalogs.Maker}'s driver catalog for this model…");
        var packages = await PackagesAsync(context.Options.Concurrency, ct).ConfigureAwait(false);
        context.Report(Name, $"{packages.Count} driver packages for this model");

        var findings = new List<Finding>();
        foreach (var device in context.Devices.Where(d => d.Version != null && d.HardwareIds.Count > 0))
        {
            var match = packages
                .Select(p => (Package: p, Version: VersionFor(p, device)))
                .Where(x => x.Version != null)
                .OrderByDescending(x => DriverVersion.ParseOrNull(x.Version))
                .ThenByDescending(x => x.Package.Released)
                .FirstOrDefault();
            if (match.Package == null)
                continue;
            findings.Add(await ToFindingAsync(match.Package, match.Version!, device, ct).ConfigureAwait(false));
        }
        return findings;
    }

    /// <summary>Every driver package the maker lists for this model.</summary>
    public async Task<IReadOnlyList<OemPackage>> PackagesAsync(int concurrency, CancellationToken ct) => catalogs.Maker switch
    {
        "Dell" => DellPackages(await catalogs.DellAsync(ct).ConfigureAwait(false), catalogs.System.Sku),
        "HP" => await HpPackagesAsync(concurrency, ct).ConfigureAwait(false),
        "Lenovo" => await LenovoPackagesAsync(concurrency, ct).ConfigureAwait(false),
        _ => [],
    };

    /// <summary>The driver version <paramref name="package"/> brings for <paramref name="device"/>, or null when it is not for this device.</summary>
    internal static string? VersionFor(OemPackage package, DeviceInfo device)
    {
        foreach (string id in package.HardwareIds)
        {
            if (!Covers(id, device))
                continue;
            return package.DriverVersions.TryGetValue(id, out string? v) ? v : package.Version;
        }
        return null;
    }

    /// <summary>
    /// True when the catalog's ID names this device: equal to one of its IDs, or a prefix ending at a
    /// separator ("PCI\VEN_8086&amp;DEV_4680" covers "PCI\VEN_8086&amp;DEV_4680&amp;SUBSYS_7D751462&amp;REV_0C").
    /// </summary>
    internal static bool Covers(string catalogId, DeviceInfo device)
    {
        foreach (string id in device.HardwareIds.Concat(device.CompatibleIds))
        {
            if (id.Length == catalogId.Length ? id.Equals(catalogId, StringComparison.OrdinalIgnoreCase)
                : id.Length > catalogId.Length && id.StartsWith(catalogId, StringComparison.OrdinalIgnoreCase) && id[catalogId.Length] is '&' or '\\')
            {
                return true;
            }
        }
        return false;
    }

    private async Task<Finding> ToFindingAsync(OemPackage p, string version, DeviceInfo device, CancellationToken ct)
    {
        bool newer = DriverVersion.ParseOrNull(version) is { } available && device.Version is { } installed && available > installed;
        var notes = new StringBuilder();
        if (newer && p.ReleaseNotesUrl != null)
        {
            string? text = await catalogs.TextAsync(p.ReleaseNotesUrl, ct).ConfigureAwait(false);
            if (text != null)
            {
                bool html = text.Contains("<html", StringComparison.OrdinalIgnoreCase) || text.Contains("<p", StringComparison.OrdinalIgnoreCase);
                string changes = TextTools.ExtractChanges(html ? TextTools.HtmlToText(text) : TextTools.NormalizeLines(text), version, 2000);
                if (changes.Length > 0)
                    notes.AppendLine(changes).AppendLine();
            }
        }
        if (notes.Length == 0 && p.Description.Length > 0)
            notes.AppendLine(p.Description).AppendLine();
        notes.Append(newer
            ? $"{p.Maker}'s driver package for this model: {p.Name} {p.Version}. Drivers from the PC maker are tested for this model."
            : $"{p.Maker}'s newest driver for this device is {version}; the installed {device.VersionText} is the same or newer.");

        return new Finding
        {
            Kind = UpdateKind.Driver,
            Status = newer ? FindingStatus.UpdateAvailable : FindingStatus.UpToDate,
            Source = p.Maker,
            Device = device.Name,
            Category = device.DeviceClass,
            DeviceInstanceId = device.InstanceId,
            InstalledVersion = device.VersionText,
            InstalledDate = device.DriverDate,
            AvailableVersion = version,
            AvailableDate = p.Released,
            Title = $"{p.Name} {p.Version}",
            Provider = p.Maker,
            Notes = notes.ToString().Trim(),
            DownloadUrl = p.DownloadUrl,
            InfoUrl = p.InfoUrl,
            ReleaseNotesUrl = p.ReleaseNotesUrl,
            Size = p.Size,
            Severity = p.Severity,
        };
    }

    // ---------- Dell ----------

    internal static IReadOnlyList<OemPackage> DellPackages(XDocument? catalog, string sku)
    {
        if (catalog == null)
            return [];
        sku = sku.Trim();
        var list = new List<OemPackage>();
        foreach (var c in catalog.Descendants().Where(e => e.Name.LocalName == "SoftwareComponent"))
        {
            if (!string.Equals((string?)Child(c, "ComponentType")?.Attribute("value"), "DRVR", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!c.Descendants().Any(m => m.Name.LocalName == "Model" && string.Equals((string?)m.Attribute("systemID"), sku, StringComparison.OrdinalIgnoreCase)))
                continue;
            var ids = new List<string>();
            foreach (var pci in c.Descendants().Where(e => e.Name.LocalName == "PCIInfo"))
            {
                string ven = (string?)pci.Attribute("vendorID") ?? "";
                string dev = (string?)pci.Attribute("deviceID") ?? "";
                if (ven.Length != 4 || dev.Length != 4)
                    continue;
                string id = $"PCI\\VEN_{ven}&DEV_{dev}".ToUpperInvariant();
                string subVen = (string?)pci.Attribute("subVendorID") ?? "";
                string subDev = (string?)pci.Attribute("subDeviceID") ?? "";
                if (subVen.Length == 4 && subDev.Length == 4)
                    id += $"&SUBSYS_{subDev}{subVen}".ToUpperInvariant();
                ids.Add(id);
            }
            foreach (var pnp in c.Descendants().Where(e => e.Name.LocalName == "PnPInfo"))
                ids.AddRange(pnp.Attributes().Select(a => a.Value.Trim()).Where(v => v.Contains('\\')));
            if (ids.Count == 0)
                continue;

            string path = (string?)c.Attribute("path") ?? "";
            list.Add(new OemPackage
            {
                Maker = "Dell",
                Name = Display(Child(c, "Name")),
                Version = (string?)c.Attribute("vendorVersion") ?? (string?)c.Attribute("dellVersion") ?? "",
                Released = DateTime.TryParse((string?)c.Attribute("releaseDate"), CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d) ? d.Date : null,
                HardwareIds = ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                Description = Display(Child(c, "Description")),
                DownloadUrl = path.Length > 0 ? OemCatalogs.DellUrl(path) : null,
                InfoUrl = (string?)Child(c, "ImportantInfo")?.Attribute("URL"),
                Size = long.TryParse((string?)c.Attribute("size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
                Severity = Display(Child(c, "Criticality")),
            });
        }
        return list;
    }

    private static XElement? Child(XElement e, string localName) => e.Elements().FirstOrDefault(x => x.Name.LocalName == localName);

    private static string Display(XElement? e) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == "Display")?.Value.Trim() ?? "";

    // ---------- HP ----------

    private async Task<IReadOnlyList<OemPackage>> HpPackagesAsync(int concurrency, CancellationToken ct)
    {
        var reference = await catalogs.HpAsync(ct).ConfigureAwait(false);
        if (reference == null)
            return [];
        var drivers = reference.Descendants("UpdateInfo")
            .Where(u => ((string?)u.Element("Category") ?? "").StartsWith("Driver", StringComparison.OrdinalIgnoreCase))
            .ToList();
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = drivers.Select(async u =>
        {
            string? cvaUrl = Https((string?)u.Element("CvaUrl"));
            if (cvaUrl == null)
                return null;
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string? cva = await catalogs.TextAsync(cvaUrl, ct).ConfigureAwait(false);
                return cva == null ? null : HpPackage(u, cva);
            }
            finally
            {
                gate.Release();
            }
        });
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<OemPackage>().ToList();
    }

    internal static OemPackage? HpPackage(XElement update, string cva)
    {
        var ids = CvaDevices(cva);
        if (ids.Count == 0)
            return null;
        return new OemPackage
        {
            Maker = "HP",
            Name = ((string?)update.Element("Name") ?? "HP driver").Trim(),
            Version = ((string?)update.Element("Version") ?? "").Trim(),
            Released = DateTime.TryParseExact((string?)update.Element("DateReleased"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
            HardwareIds = ids,
            Description = ((string?)update.Element("Description") ?? "").Trim(),
            DownloadUrl = Https((string?)update.Element("Url")),
            InfoUrl = "https://support.hp.com/drivers",
            ReleaseNotesUrl = Https((string?)update.Element("ReleaseNotesUrl")),
            Size = long.TryParse((string?)update.Element("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
            Severity = ((string?)update.Element("ReleaseType") ?? "").Trim(),
        };
    }

    /// <summary>Hardware IDs from a SoftPaq .cva file's [Devices] section ("USB\VID_8087&amp;PID_0033="Intel Wi-Fi 6E AX211"").</summary>
    internal static IReadOnlyList<string> CvaDevices(string cva)
    {
        var ids = new List<string>();
        bool inDevices = false;
        foreach (string raw in cva.Split('\n'))
        {
            string line = raw.Trim();
            if (line.StartsWith('['))
            {
                inDevices = line.Equals("[Devices]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inDevices || line.Length == 0 || line.StartsWith(';'))
                continue;
            int eq = line.IndexOf('=');
            string id = (eq > 0 ? line[..eq] : line).Trim();
            if (id.Contains('\\'))
                ids.Add(id.ToUpperInvariant());
        }
        return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? Https(string? url) =>
        string.IsNullOrWhiteSpace(url) ? null : url.Contains("://", StringComparison.Ordinal) ? url.Trim() : "https://" + url.Trim();

    // ---------- Lenovo ----------

    private async Task<IReadOnlyList<OemPackage>> LenovoPackagesAsync(int concurrency, CancellationToken ct)
    {
        var catalog = await catalogs.LenovoAsync(ct).ConfigureAwait(false);
        if (catalog == null)
            return [];
        var locations = catalog.Descendants("package")
            .Where(p => !((string?)p.Element("category") ?? "").Contains("BIOS", StringComparison.OrdinalIgnoreCase))
            .Select(p => ((string?)p.Element("location") ?? "").Trim())
            .Where(l => l.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            .ToList();
        using var gate = new SemaphoreSlim(Math.Max(1, concurrency));
        var tasks = locations.Select(async location =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                string? xml = await catalogs.TextAsync(location, ct).ConfigureAwait(false);
                return xml == null ? null : LenovoPackage(xml, location, catalogs.System.Model);
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
            finally
            {
                gate.Release();
            }
        });
        return (await Task.WhenAll(tasks).ConfigureAwait(false)).OfType<OemPackage>().ToList();
    }

    internal static OemPackage? LenovoPackage(string xml, string location, string model)
    {
        var root = XDocument.Parse(xml).Root;
        if (root == null || root.Name.LocalName != "Package")
            return null;

        var versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in root.Descendants("_Driver"))
        {
            string version = VersionMarkRegex().Replace(((string?)driver.Element("Version") ?? "").Trim(), "");
            foreach (var hw in driver.Elements("HardwareID"))
            {
                string id = hw.Value.Trim();
                if (id.Contains('\\') && version.Length > 0)
                    versions.TryAdd(id, version);
            }
        }
        var ids = versions.Keys
            .Concat(root.Descendants("_PnPID").Select(e => e.Value.Trim()).Where(v => v.Contains('\\')))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (ids.Count == 0)
            return null;

        string baseUrl = location[..(location.LastIndexOf('/') + 1)];
        var installer = root.Element("Files")?.Element("Installer")?.Element("File");
        var readme = root.Element("Files")?.Element("Readme")?.Element("File");
        string? Url(string? name) => string.IsNullOrWhiteSpace(name) ? null : baseUrl + name.Trim();

        return new OemPackage
        {
            Maker = "Lenovo",
            Name = root.Element("Title")?.Elements("Desc").FirstOrDefault()?.Value.Trim() ?? "Lenovo driver",
            Version = (string?)root.Attribute("version") ?? "",
            Released = DateTime.TryParseExact((string?)root.Element("ReleaseDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
            HardwareIds = ids,
            DriverVersions = versions,
            DownloadUrl = Url((string?)installer?.Element("Name")),
            InfoUrl = $"https://pcsupport.lenovo.com/search?query={Uri.EscapeDataString(model)}",
            ReleaseNotesUrl = Url((string?)readme?.Element("Name")),
            Size = long.TryParse((string?)installer?.Element("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
            Severity = ((string?)root.Element("Severity")?.Attribute("type")) switch
            {
                "1" => "Critical",
                "2" => "Recommended",
                "3" => "Optional",
                _ => "",
            },
        };
    }

    // "1.0.4.2^" means "1.0.4.2 or later"
    [GeneratedRegex(@"[\^\s]+$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex VersionMarkRegex();
}
