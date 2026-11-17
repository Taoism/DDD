using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDD.Core.Oem;

namespace DDD.Core.Bios;

/// <summary>Lenovo's per-model catalogs (download.lenovo.com/catalog/&lt;type&gt;_Win11.xml), the ones Lenovo System Update reads.</summary>
internal static partial class LenovoBios
{
    internal sealed record Package(
        string Id,
        string Title,
        string Version,
        DateTime? Released,
        string? InstallerUrl,
        long? Size,
        string? ReadmeUrl,
        IReadOnlyList<string> BiosLevels,
        string Severity);

    public static async Task<Finding?> CheckAsync(OemCatalogs catalogs, CancellationToken ct)
    {
        var system = catalogs.System;
        var catalog = await catalogs.LenovoAsync(ct).ConfigureAwait(false);
        if (catalog == null)
            return null;

        Package? best = null;
        foreach (string location in BiosLocations(catalog))
        {
            string? xml = await catalogs.TextAsync(location, ct).ConfigureAwait(false);
            var package = xml == null ? null : ParsePackage(xml, location);
            if (package == null || !FitsInstalled(package, system.BiosVersion))
                continue;
            if (best == null || DriverVersion.ParseOrNull(package.Version) > DriverVersion.ParseOrNull(best.Version))
                best = package;
        }
        if (best == null)
            return null;

        string installed = InstalledVersion(system.BiosVersion);
        string notes = "";
        if (best.ReadmeUrl != null)
        {
            try
            {
                string readme = await Http.GetStringAsync(best.ReadmeUrl, ct).ConfigureAwait(false);
                notes = TextTools.ExtractChanges(best.ReadmeUrl.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? TextTools.NormalizeLines(readme) : TextTools.HtmlToText(readme), best.Version);
            }
            catch (HttpRequestException)
            {
                // notes are optional
            }
        }
        if (notes.Length == 0)
            notes = $"{best.Title} version {best.Version}. See the readme for the change list.";

        return BiosSource.Make(system, "Lenovo", BiosSource.IsNewer(installed, best.Version, system.BiosDate, best.Released),
            installed, best.Version, best.Released, best.Title, notes, best.InstallerUrl,
            $"https://pcsupport.lenovo.com/search?query={Uri.EscapeDataString(system.Model)}", best.ReadmeUrl, best.Size, best.Severity);
    }

    internal static IEnumerable<string> BiosLocations(XDocument catalog) =>
        catalog.Descendants("package")
            .Where(p => ((string?)p.Element("category") ?? "").Contains("BIOS", StringComparison.OrdinalIgnoreCase))
            .Select(p => ((string?)p.Element("location") ?? "").Trim())
            .Where(l => l.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || l.StartsWith("http://", StringComparison.OrdinalIgnoreCase));

    internal static Package? ParsePackage(string xml, string location)
    {
        var root = XDocument.Parse(xml).Root;
        if (root == null || root.Name.LocalName != "Package")
            return null;
        string baseUrl = location[..(location.LastIndexOf('/') + 1)];
        string? Url(string? name) => string.IsNullOrWhiteSpace(name) ? null : baseUrl + name.Trim();

        var installer = root.Element("Files")?.Element("Installer")?.Element("File");
        var readme = root.Element("Files")?.Element("Readme")?.Element("File");
        var levels = root.Element("DetectInstall")?.Descendants("Level").Select(l => l.Value.Trim())
            .Concat(root.Element("Dependencies")?.Descendants("Level").Select(l => l.Value.Trim()) ?? [])
            .ToList() ?? [];

        return new Package(
            (string?)root.Attribute("id") ?? "",
            root.Element("Title")?.Elements("Desc").FirstOrDefault()?.Value.Trim() ?? "BIOS Update",
            (string?)root.Attribute("version") ?? "",
            DateTime.TryParseExact((string?)root.Element("ReleaseDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
            Url((string?)installer?.Element("Name")),
            long.TryParse((string?)installer?.Element("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
            Url((string?)readme?.Element("Name")),
            levels,
            ((string?)root.Element("Severity")?.Attribute("type")) switch
            {
                "1" => "Critical",
                "2" => "Recommended",
                "3" => "Optional",
                _ => "",
            });
    }

    /// <summary>"N32ET96W (1.72 )" → "1.72"; anything else is returned trimmed.</summary>
    internal static string InstalledVersion(string smbiosVersion)
    {
        var m = InstalledVersionRegex().Match(smbiosVersion);
        return m.Success ? m.Groups[1].Value : smbiosVersion.Trim();
    }

    /// <summary>The package is for this machine's BIOS family: its levels ("N32ETA2*") share the installed BIOS ID's first five characters.</summary>
    internal static bool FitsInstalled(Package package, string smbiosVersion)
    {
        if (package.BiosLevels.Count == 0 || smbiosVersion.Length < 5)
            return true;
        string family = smbiosVersion[..5];
        return package.BiosLevels.Any(l => l.StartsWith(family, StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\(\s*([\d.]+)\s*\)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex InstalledVersionRegex();
}
