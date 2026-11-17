using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDD.Core.Bios;

namespace DDD.Core.Oem;

/// <summary>
/// The PC maker's update catalog for this model (Dell, HP, Lenovo), downloaded once per scan and
/// shared by the BIOS check and the driver check.
/// </summary>
public sealed partial class OemCatalogs(SystemInfo system)
{
    private const string DellDownloads = "https://downloads.dell.com/";

    private readonly ConcurrentDictionary<string, Task<XDocument?>> _docs = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<string?>> _texts = new(StringComparer.OrdinalIgnoreCase);

    public SystemInfo System { get; } = system;

    /// <summary>"Dell", "HP", "Lenovo", or "" when the maker has no catalog DDD reads.</summary>
    public string Maker { get; } = Vendors.ShortName(system.Manufacturer) switch
    {
        "Dell" => "Dell",
        "HP" => "HP",
        "Lenovo" => "Lenovo",
        _ => "",
    };

    public static string DellUrl(string path) => DellDownloads + path.TrimStart('/');

    /// <summary>The model's Dell catalog (by system SKU), or null when the model isn't in Dell's index.</summary>
    public Task<XDocument?> DellAsync(CancellationToken ct) => Once("dell", async () =>
    {
        string sku = System.Sku.Trim().ToUpperInvariant();
        if (!DellSkuRegex().IsMatch(sku))
            return null;
        var index = await Cab.DownloadXmlAsync(DellDownloads + "catalog/CatalogIndexPC.cab", ct).ConfigureAwait(false);
        string? path = DellBios.ModelCatalogPath(index, sku);
        return path == null ? null : await Cab.DownloadXmlAsync(DellUrl(path), ct).ConfigureAwait(false);
    });

    /// <summary>The model's HP reference catalog (by platform ID) for the closest Windows release, or null.</summary>
    public Task<XDocument?> HpAsync(CancellationToken ct) => Once("hp", async () =>
    {
        string platform = System.BoardProduct.Trim().ToLowerInvariant();
        if (!HpPlatformRegex().IsMatch(platform))
            return null;
        foreach (string os in HpBios.OsCandidates(System))
        {
            try
            {
                return await Cab.DownloadXmlAsync($"https://hpia.hpcloud.hp.com/ref/{platform}/{platform}_64_{os}.cab", ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (BiosSource.IsNotFound(ex))
            {
                // no catalog for this Windows release; try an older one
            }
        }
        return null;
    });

    /// <summary>The model's Lenovo catalog (by machine type, the first four characters of the model), or null.</summary>
    public Task<XDocument?> LenovoAsync(CancellationToken ct) => Once("lenovo", async () =>
    {
        if (System.Model.Length < 4)
            return null;
        string machineType = System.Model[..4].ToUpperInvariant();
        foreach (string os in System.IsWindows11 ? (string[])["Win11", "Win10"] : ["Win10"])
        {
            try
            {
                return XDocument.Parse(await Http.GetStringAsync($"https://download.lenovo.com/catalog/{machineType}_{os}.xml", ct).ConfigureAwait(false));
            }
            catch (HttpRequestException ex) when (BiosSource.IsNotFound(ex))
            {
                // try the next Windows generation
            }
        }
        return null;
    });

    /// <summary>A small text file (Lenovo package descriptor, HP .cva), fetched once; null when it can't be read.</summary>
    public Task<string?> TextAsync(string url, CancellationToken ct) =>
        _texts.GetOrAdd(url, async u =>
        {
            try
            {
                return await Http.GetStringAsync(u, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                return null;
            }
        });

    private Task<XDocument?> Once(string key, Func<Task<XDocument?>> load) => _docs.GetOrAdd(key, _ => load());

    [GeneratedRegex("^[0-9A-F]{4}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DellSkuRegex();

    [GeneratedRegex("^[0-9a-f]{4}$", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex HpPlatformRegex();
}
