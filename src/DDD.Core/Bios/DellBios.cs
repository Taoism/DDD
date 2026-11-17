using System.Globalization;
using System.Xml.Linq;
using DDD.Core.Oem;

namespace DDD.Core.Bios;

/// <summary>Dell's update catalogs (downloads.dell.com/catalog), the ones Dell Command | Update reads. The model is the system SKU (e.g. "0A5C").</summary>
internal static class DellBios
{
    internal sealed record Component(
        string Name,
        string VendorVersion,
        DateTime? Released,
        string Path,
        long? Size,
        string Description,
        string? InfoUrl,
        string Criticality,
        string? Sha256 = null);

    public static async Task<Finding?> CheckAsync(OemCatalogs catalogs, CancellationToken ct)
    {
        var system = catalogs.System;
        var model = await catalogs.DellAsync(ct).ConfigureAwait(false);
        if (model == null)
            return null;
        string sku = system.Sku.Trim().ToUpperInvariant();
        var best = BiosComponents(model, sku)
            .OrderByDescending(c => DriverVersion.ParseOrNull(c.VendorVersion))
            .ThenByDescending(c => c.Released)
            .FirstOrDefault();
        if (best == null)
            return null;

        string installed = system.BiosVersion.Trim();
        string notes = best.Description;
        if (best.Criticality.Length > 0)
            notes += $"\n\nDell rates this update: {best.Criticality}.";
        notes += "\n\nDell lists the fixes and enhancements of each version on the driver page (Details link).";
        return BiosSource.Make(system, "Dell", BiosSource.IsNewer(installed, best.VendorVersion, system.BiosDate, best.Released),
            installed, best.VendorVersion, best.Released, best.Name, notes, OemCatalogs.DellUrl(best.Path),
            best.InfoUrl, null, best.Size, best.Criticality, best.Sha256);
    }

    /// <summary>Path of the model's own catalog inside the index, by system ID.</summary>
    internal static string? ModelCatalogPath(XDocument index, string sku) =>
        index.Descendants().Where(e => e.Name.LocalName == "GroupManifest")
            .Where(g => g.Descendants().Any(m => m.Name.LocalName == "Model" && string.Equals((string?)m.Attribute("systemID"), sku, StringComparison.OrdinalIgnoreCase)))
            .Select(g => g.Elements().FirstOrDefault(e => e.Name.LocalName == "ManifestInformation"))
            .Select(m => (string?)m?.Attribute("path"))
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

    internal static IReadOnlyList<Component> BiosComponents(XDocument catalog, string sku)
    {
        var list = new List<Component>();
        foreach (var c in catalog.Descendants().Where(e => e.Name.LocalName == "SoftwareComponent"))
        {
            var type = Child(c, "ComponentType");
            if (!string.Equals((string?)type?.Attribute("value"), "BIOS", StringComparison.OrdinalIgnoreCase))
                continue;
            bool forThisModel = c.Descendants().Any(m => m.Name.LocalName == "Model"
                && string.Equals((string?)m.Attribute("systemID"), sku, StringComparison.OrdinalIgnoreCase));
            if (!forThisModel)
                continue;
            list.Add(new Component(
                Display(Child(c, "Name")),
                (string?)c.Attribute("vendorVersion") ?? (string?)c.Attribute("dellVersion") ?? "",
                DateTime.TryParse((string?)c.Attribute("releaseDate"), CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var d) ? d.Date : null,
                (string?)c.Attribute("path") ?? "",
                long.TryParse((string?)c.Attribute("size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
                Display(Child(c, "Description")),
                (string?)Child(c, "ImportantInfo")?.Attribute("URL"),
                Display(Child(c, "Criticality")),
                Oem.OemDriverSource.DellSha256(c)));
        }
        return list;
    }

    private static XElement? Child(XElement e, string localName) => e.Elements().FirstOrDefault(x => x.Name.LocalName == localName);

    private static string Display(XElement? e) =>
        e?.Elements().FirstOrDefault(x => x.Name.LocalName == "Display")?.Value.Trim() ?? "";

}
