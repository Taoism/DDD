using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DDD.Core.Sources;

/// <summary>
/// GeForce drivers straight from NVIDIA: the lookup the NVIDIA app uses (by PCI device ID) and the
/// driver-details service behind nvidia.com's download pages, which carries the release notes.
/// </summary>
public sealed partial class NvidiaSource : IUpdateSource
{
    public const string SourceName = "NVIDIA";

    private const string LookupBase = "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/";
    private const string DetailsBase = "https://www.nvidia.com/services/com.nvidia.services/AEMDriversContent/getDownloadDetails?";

    public string Name => SourceName;

    public static bool IsNvidiaGpu(DeviceInfo d) =>
        d.HardwareIds.Count > 0 && d.HardwareIds[0].StartsWith("PCI\\VEN_10DE&", StringComparison.OrdinalIgnoreCase)
        && d.DeviceClass.Equals("Display", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct)
    {
        var findings = new List<Finding>();
        foreach (var gpu in context.Devices.Where(IsNvidiaGpu).DistinctBy(d => d.HardwareIds[0], StringComparer.OrdinalIgnoreCase))
        {
            var m = DeviceIdRegex().Match(gpu.HardwareIds[0]);
            if (!m.Success)
                continue;
            context.Report(Name, $"Looking up {gpu.Name}…");
            string json = await Http.GetStringAsync(LookupUrl(m.Groups[1].Value, context.System, context.Options.NvidiaStudio), ct).ConfigureAwait(false);
            var latest = ParseLookup(json);
            if (latest == null)
                continue;

            string notes = "";
            long? size = null;
            try
            {
                string details = await Http.GetStringAsync(DetailsBase + Uri.EscapeDataString($"{{\"ddID\":\"{latest.Id}\"}}"), ct).ConfigureAwait(false);
                (notes, size) = ParseDetails(details);
            }
            catch (HttpRequestException)
            {
                // the lookup alone still gives version and links
            }

            // NVIDIA has used this name for years, but only link it when it is really there
            string? pdf = $"https://us.download.nvidia.com/Windows/{latest.Version}/{latest.Version}-win11-win10-release-notes.pdf";
            if (!await Http.ExistsAsync(pdf, ct).ConfigureAwait(false))
                pdf = null;

            string installed = InstalledVersion(gpu.VersionText) ?? gpu.VersionText;
            bool newer = DriverVersion.ParseOrNull(latest.Version) > DriverVersion.ParseOrNull(installed);
            findings.Add(new Finding
            {
                Kind = UpdateKind.Driver,
                Status = newer ? FindingStatus.UpdateAvailable : FindingStatus.UpToDate,
                Source = Name,
                Device = gpu.Name,
                Category = gpu.DeviceClass,
                DeviceInstanceId = gpu.InstanceId,
                InstalledVersion = installed,
                InstalledDate = gpu.DriverDate,
                AvailableVersion = latest.Version,
                AvailableDate = latest.Released,
                Title = $"{latest.Name} {latest.Version}",
                Provider = "NVIDIA",
                Notes = notes.Length > 0 ? notes : $"{latest.Name} {latest.Version}. See NVIDIA's page for what it adds and fixes.",
                DownloadUrl = latest.DownloadUrl,
                InfoUrl = latest.DetailsUrl,
                ReleaseNotesUrl = pdf,
                Size = size,
                Severity = latest.IsWhql ? "WHQL" : "",
            });
        }
        return findings;
    }

    internal static string LookupUrl(string deviceId, SystemInfo system, bool studio)
    {
        var query = new Dictionary<string, object>
        {
            ["dIDa"] = new[] { deviceId.ToUpperInvariant() + "_10DE" },
            ["osC"] = "10.0",
            ["osB"] = system.OsBuild.ToString(CultureInfo.InvariantCulture),
            ["is6"] = Environment.Is64BitOperatingSystem ? "1" : "0",
            ["lg"] = "1033",
            ["iLp"] = system.IsLaptop ? "1" : "0",
            ["prvMd"] = "0",
            ["gcV"] = "2.11.4.6",
            ["gIsB"] = "0",
            ["dch"] = "1",
            ["upCRD"] = "0",
            ["isCRD"] = studio ? "1" : "0",
        };
        return LookupBase + Uri.EscapeDataString(JsonSerializer.Serialize(query));
    }

    /// <summary>Installed driver "32.0.16.2124" → NVIDIA's own "621.24" (last five digits of the last two parts).</summary>
    public static string? InstalledVersion(string windowsVersion)
    {
        string[] parts = windowsVersion.Split('.');
        if (parts.Length != 4 || !parts[2].All(char.IsAsciiDigit) || !parts[3].All(char.IsAsciiDigit) || parts[3].Length == 0)
            return null;
        string digits = parts[2] + parts[3].PadLeft(4, '0');
        if (digits.Length < 5)
            return null;
        digits = digits[^5..];
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    internal sealed record NvidiaDriver(string Id, string Name, string Version, DateTime? Released, string? DownloadUrl, string? DetailsUrl, bool IsWhql);

    internal static NvidiaDriver? ParseLookup(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("DriverAttributes", out var a) || a.ValueKind != JsonValueKind.Object)
            return null;
        string version = Str(a, "Version");
        if (version.Length == 0)
            return null;
        string admin = Str(a, "DownloadURLAdmin");
        return new NvidiaDriver(
            a.TryGetProperty("ID", out var id) ? id.ToString() : "",
            Str(a, "Name") is { Length: > 0 } n ? n : "GeForce driver",
            version,
            DateTimeOffset.TryParse(Str(a, "ReleaseDateTime"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var released) ? released.Date : null,
            admin.Length > 0 ? admin : NullIfEmpty(Str(a, "DownloadURL")),
            NullIfEmpty(Str(a, "DetailsURL")),
            a.TryGetProperty("IsWHQL", out var whql) && whql.ToString() == "1");
    }

    /// <summary>Release highlights and fixed bugs as plain text, plus the download size.</summary>
    internal static (string Notes, long? Size) ParseDetails(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("driverDetails", out var dd) || !dd.TryGetProperty("IDS", out var ids)
            || ids.ValueKind != JsonValueKind.Array || ids.GetArrayLength() == 0
            || !ids[0].TryGetProperty("downloadInfo", out var info))
        {
            return ("", null);
        }
        var sb = new StringBuilder();
        string releaseNotes = Decode(Str(info, "ReleaseNotes"));
        if (releaseNotes.Length > 0)
            sb.Append(TextTools.HtmlToText(releaseNotes));
        string other = TextTools.HtmlToText(Decode(Str(info, "OtherNotes")));
        if (other.Length > 0 && !other.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            sb.AppendLine().AppendLine().Append(other);
        return (TextTools.Cut(sb.ToString(), 6000), ParseSize(Str(info, "DownloadURLFileSize")));
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return value;
        }
    }

    /// <summary>"1.02 GB" / "980.5 MB" → bytes.</summary>
    internal static long? ParseSize(string text)
    {
        var m = SizeRegex().Match(text);
        if (!m.Success || !double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            return null;
        double factor = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "KB" => 1024d,
            "MB" => 1024d * 1024,
            "GB" => 1024d * 1024 * 1024,
            _ => 1,
        };
        return (long)(value * factor);
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString().Trim() : "";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex DeviceIdRegex();

    [GeneratedRegex(@"([\d.]+)\s*(KB|MB|GB)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SizeRegex();
}
