using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DDD.Core.Sources;

/// <summary>The catalog kept answering "Too many search requests".</summary>
public sealed class CatalogThrottledException()
    : HttpRequestException("The Microsoft Update Catalog is limiting searches from this PC. Try again in a few minutes.");

/// <summary>One row of a Microsoft Update Catalog search.</summary>
public sealed record CatalogEntry(
    string UpdateId,
    string Title,
    string Products,
    string Classification,
    DateTime? LastUpdated,
    string VersionText,
    long? Size)
{
    public DriverVersion? Version => DriverVersion.ParseOrNull(VersionText) ?? DriverVersion.FindIn(Title);
}

/// <summary>The catalog's details page for one update.</summary>
public sealed record CatalogDetails(
    string UpdateId,
    string Title,
    string Description,
    string Manufacturer,
    string Provider,
    string DriverClass,
    string DriverModel,
    string VersionText,
    DateTime? VersionDate,
    string Architecture,
    string? MoreInfoUrl,
    string? SupportUrl);

/// <summary>
/// Reads the Microsoft Update Catalog (catalog.update.microsoft.com), the public home of every
/// driver shipped through Windows Update. It has no API, so the HTML pages are parsed.
/// </summary>
public sealed partial class CatalogClient
{
    private const string Base = "https://www.catalog.update.microsoft.com/";
    private static readonly CultureInfo Us = CultureInfo.GetCultureInfo("en-US");

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<CatalogEntry>>> _searches = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Task<CatalogDetails?>> _details = new(StringComparer.OrdinalIgnoreCase);

    public static string SearchUrl(string query) => Base + "Search.aspx?q=" + Uri.EscapeDataString(query);

    public static string DetailsUrl(string updateId) => Base + "ScopedViewInline.aspx?updateid=" + Uri.EscapeDataString(updateId);

    /// <summary>Called with a message while the catalog asks DDD to slow down.</summary>
    public Action<string>? OnThrottled { get; set; }

    /// <summary>The catalog kept refusing searches; later searches fail at once instead of piling on.</summary>
    public bool GaveUp => _gaveUp;

    /// <summary>
    /// Search results for <paramref name="query"/> (cached for the client's lifetime; failures are not cached).
    /// Only the first page (25 rows) is read: the catalog lists the most recently updated drivers first.
    /// </summary>
    public Task<IReadOnlyList<CatalogEntry>> SearchAsync(string query, CancellationToken ct) =>
        Cached(_searches, query, q => SearchCoreAsync(q, ct));

    public Task<CatalogDetails?> GetDetailsAsync(string updateId, CancellationToken ct) =>
        Cached(_details, updateId, id => DetailsCoreAsync(id, ct));

    private static async Task<T> Cached<T>(ConcurrentDictionary<string, Task<T>> cache, string key, Func<string, Task<T>> load)
    {
        var task = cache.GetOrAdd(key, load);
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            cache.TryRemove(new KeyValuePair<string, Task<T>>(key, task));
            throw;
        }
    }

    // The catalog answers "Too many search requests" when searched quickly, and that block lasts
    // minutes. Searches therefore go one at a time with a pause between them.
    private static readonly TimeSpan SearchSpacing = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan[] ThrottleWaits = [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60)];
    private readonly SemaphoreSlim _searchGate = new(1, 1);
    private DateTime _lastSearch = DateTime.MinValue;
    private volatile bool _gaveUp;

    /// <summary>Folder for cached search pages; null turns the disk cache off.</summary>
    public string? CacheFolder { get; init; } = DefaultCacheFolder;

    public static string DefaultCacheFolder => AppPaths.CacheFolder;

    /// <summary>How long a cached search page is reused. A rescan soon after a scan then costs no searches.</summary>
    public TimeSpan CacheAge { get; init; } = TimeSpan.FromHours(6);

    private string? CachePath(string query)
    {
        if (CacheFolder == null)
            return null;
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(query.ToUpperInvariant()));
        return Path.Combine(CacheFolder, Convert.ToHexString(hash, 0, 12) + ".html");
    }

    private async Task<string> GetSearchPageAsync(string query, CancellationToken ct)
    {
        string? cachePath = CachePath(query);
        if (cachePath != null && File.Exists(cachePath) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath) < CacheAge)
        {
            try
            {
                return await File.ReadAllTextAsync(cachePath, ct).ConfigureAwait(false);
            }
            catch (IOException)
            {
                // fall through to the network
            }
        }
        string html = await DownloadSearchPageAsync(query, ct).ConfigureAwait(false);
        if (cachePath != null)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder!);
                await File.WriteAllTextAsync(cachePath, html, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // caching is only an optimisation
            }
        }
        return html;
    }

    /// <summary>Removes cached search pages, so the next scan asks the catalog again.</summary>
    public static void ClearCache(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        foreach (string file in Directory.EnumerateFiles(folder, "*.html"))
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // in use; it expires on its own
            }
        }
    }

    private async Task<string> DownloadSearchPageAsync(string query, CancellationToken ct)
    {
        await _searchGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                if (_gaveUp)
                    throw new CatalogThrottledException();
                var wait = _lastSearch + SearchSpacing - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, ct).ConfigureAwait(false);
                _lastSearch = DateTime.UtcNow;
                string html = await Http.GetStringAsync(SearchUrl(query), ct).ConfigureAwait(false);
                if (!IsThrottled(html))
                    return html;
                if (attempt >= ThrottleWaits.Length)
                {
                    _gaveUp = true;
                    throw new CatalogThrottledException();
                }
                OnThrottled?.Invoke($"The catalog asks to slow down; waiting {ThrottleWaits[attempt].TotalSeconds:0} s…");
                await Task.Delay(ThrottleWaits[attempt], ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _searchGate.Release();
        }
    }

    internal static bool IsThrottled(string html) => html.Contains("catalogBody_searchThrottled", StringComparison.Ordinal);

    /// <summary>Direct download links (.cab) for an update, from the catalog's download dialog.</summary>
    public static async Task<IReadOnlyList<string>> GetDownloadUrlsAsync(string updateId, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(new[] { new DownloadRequest(0, "", updateId, updateId) });
        string html = await Http.PostFormAsync(Base + "DownloadDialog.aspx", [new("updateIDs", json)], ct).ConfigureAwait(false);
        return ParseDownloadUrls(html);
    }

    private sealed record DownloadRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("size")] int Size,
        [property: System.Text.Json.Serialization.JsonPropertyName("languages")] string Languages,
        [property: System.Text.Json.Serialization.JsonPropertyName("uidInfo")] string UidInfo,
        [property: System.Text.Json.Serialization.JsonPropertyName("updateID")] string UpdateId);

    private async Task<IReadOnlyList<CatalogEntry>> SearchCoreAsync(string query, CancellationToken ct)
    {
        string html = await GetSearchPageAsync(query, ct).ConfigureAwait(false);
        return ParseSearch(html);
    }

    private static async Task<CatalogDetails?> DetailsCoreAsync(string updateId, CancellationToken ct)
    {
        string html = await Http.GetStringAsync(DetailsUrl(updateId), ct).ConfigureAwait(false);
        return ParseDetails(updateId, html);
    }

    internal static IReadOnlyList<CatalogEntry> ParseSearch(string html)
    {
        var entries = new List<CatalogEntry>();
        foreach (Match row in RowRegex().Matches(html))
        {
            string id = row.Groups["id"].Value;
            string rowNumber = row.Groups["r"].Value;
            var cells = new Dictionary<int, string>();
            foreach (Match cell in CellRegex().Matches(row.Groups["body"].Value))
            {
                if (cell.Groups["id"].Value.Equals(id, StringComparison.OrdinalIgnoreCase) && cell.Groups["r"].Value == rowNumber)
                    cells[int.Parse(cell.Groups["c"].Value, CultureInfo.InvariantCulture)] = cell.Groups["html"].Value;
            }
            if (!cells.TryGetValue(1, out string? titleCell))
                continue;

            long? size = null;
            if (cells.TryGetValue(6, out string? sizeCell))
            {
                var original = OriginalSizeRegex().Match(sizeCell);
                if (original.Success && long.TryParse(original.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out long bytes))
                    size = bytes;
            }

            entries.Add(new CatalogEntry(
                id,
                TextTools.Clean(titleCell),
                TextTools.Clean(cells.GetValueOrDefault(2)),
                TextTools.Clean(cells.GetValueOrDefault(3)),
                ParseDate(TextTools.Clean(cells.GetValueOrDefault(4))),
                TextTools.Clean(cells.GetValueOrDefault(5)),
                size));
        }
        return entries;
    }

    internal static CatalogDetails? ParseDetails(string updateId, string html)
    {
        string title = Span(html, "titleText");
        if (title.Length == 0)
            return null;
        string arch = "";
        var archMatch = ArchRegex().Match(html);
        if (archMatch.Success)
            arch = TextTools.Clean(archMatch.Groups[1].Value).Replace("Architecture:", "", StringComparison.OrdinalIgnoreCase).Trim();

        return new CatalogDetails(
            updateId,
            title,
            Span(html, "desc"),
            Span(html, "manufacturer"),
            Span(html, "driverProvider"),
            Span(html, "driverClass"),
            Span(html, "driverModel"),
            Span(html, "version"),
            ParseDate(Span(html, "versionDate")),
            arch,
            Link(MoreInfoRegex().Match(html)),
            Link(SupportUrlRegex().Match(html)));
    }

    internal static IReadOnlyList<string> ParseDownloadUrls(string html)
    {
        var urls = DownloadUrlRegex().Matches(html).Select(m => m.Groups[1].Value).ToList();
        if (urls.Count == 0)
            urls = AnyPackageUrlRegex().Matches(html).Select(m => m.Value).ToList();
        return urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Span(string html, string id)
    {
        var m = Regex.Match(html, "id=\"ScopedViewHandler_" + id + "\"[^>]*>(.*?)</span>",
            RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
        string value = m.Success ? TextTools.Clean(m.Groups[1].Value) : "";
        return value == "n/a" ? "" : value;
    }

    private static string? Link(Match m)
    {
        if (!m.Success)
            return null;
        string url = System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim();
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? url : null;
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, Us, DateTimeStyles.None, out var date) ? date.Date : null;

    [GeneratedRegex(@"<tr id=""(?<id>[0-9a-f-]{36})_R(?<r>\d+)"">(?<body>.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 5000)]
    private static partial Regex RowRegex();

    [GeneratedRegex(@"<td\b[^>]*\bid=""(?<id>[0-9a-f-]{36})_C(?<c>\d+)_R(?<r>\d+)""[^>]*>(?<html>.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 5000)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"_originalSize"">(\d+)<", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex OriginalSizeRegex();

    [GeneratedRegex(@"<div id=""archDiv""[^>]*>(.*?)</div>", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ArchRegex();

    [GeneratedRegex(@"id=""moreInfoDiv"".*?href=""([^""]+)""", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex MoreInfoRegex();

    // the catalog spells it "suportUrlDiv"
    [GeneratedRegex(@"id=""supp?ortUrlDiv"".*?href=""([^""]+)""", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SupportUrlRegex();

    [GeneratedRegex(@"\.url\s*=\s*'([^']+)'", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex DownloadUrlRegex();

    [GeneratedRegex(@"https?://[^'""\s]+\.(?:cab|exe|msu|zip)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex AnyPackageUrlRegex();
}
