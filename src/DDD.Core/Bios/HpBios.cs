using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DDD.Core.Oem;

namespace DDD.Core.Bios;

/// <summary>HP's reference catalogs (hpia.hpcloud.hp.com/ref), the ones HP Image Assistant reads. The platform ID is the board product (e.g. "8723").</summary>
internal static partial class HpBios
{
    internal sealed record Update(
        string Id,
        string Name,
        string Version,
        DateTime? Released,
        string? Url,
        string? ReleaseNotesUrl,
        long? Size,
        string Description,
        string Family,
        string ReleaseType);

    public static async Task<Finding?> CheckAsync(OemCatalogs catalogs, CancellationToken ct)
    {
        var system = catalogs.System;
        var reference = await catalogs.HpAsync(ct).ConfigureAwait(false);
        if (reference == null)
            return null;

        var (family, installed) = InstalledVersion(system.BiosVersion);
        var updates = BiosUpdates(reference);
        var best = updates.FirstOrDefault(u => family.Length > 0 && u.Family.Equals(family, StringComparison.OrdinalIgnoreCase))
            ?? (updates.Count == 1 ? updates[0] : null);
        if (best == null)
            return null;

        string notes = best.Description;
        if (best.ReleaseNotesUrl != null)
        {
            try
            {
                string html = await Http.GetStringAsync(best.ReleaseNotesUrl, ct).ConfigureAwait(false);
                string changes = TextTools.ExtractChanges(TextTools.HtmlToText(html), best.Version);
                if (changes.Length > 0)
                    notes = changes;
            }
            catch (HttpRequestException)
            {
                // fall back to the description
            }
        }
        return BiosSource.Make(system, "HP", BiosSource.IsNewer(installed, best.Version, system.BiosDate, best.Released),
            installed, best.Version, best.Released, best.Name, notes, best.Url,
            $"https://support.hp.com/drivers", best.ReleaseNotesUrl, best.Size, best.ReleaseType);
    }

    /// <summary>"11.0.25h2", "11.0.24h2" ... newest first, starting at this PC's release.</summary>
    internal static IEnumerable<string> OsCandidates(SystemInfo system)
    {
        if (!system.IsWindows11)
            return ["10.0.22h2", "10.0.21h2"];
        string[] all = ["11.0.25h2", "11.0.24h2", "11.0.23h2", "11.0.22h2", "11.0.21h2"];
        int start = system.OsBuild switch
        {
            >= 26200 => 0,
            >= 26100 => 1,
            >= 22631 => 2,
            >= 22621 => 3,
            _ => 4,
        };
        return all[start..];
    }

    /// <summary>"P62 Ver. 02.89" → ("P62", "02.89").</summary>
    internal static (string Family, string Version) InstalledVersion(string smbiosVersion)
    {
        var m = InstalledRegex().Match(smbiosVersion);
        return m.Success ? (m.Groups[1].Value, m.Groups[2].Value) : ("", smbiosVersion.Trim());
    }

    internal static IReadOnlyList<Update> BiosUpdates(XDocument reference)
    {
        static string? Https(string? url) =>
            string.IsNullOrWhiteSpace(url) ? null : url.Contains("://", StringComparison.Ordinal) ? url.Trim() : "https://" + url.Trim();

        return reference.Descendants("UpdateInfo")
            .Where(u => string.Equals((string?)u.Element("Category"), "BIOS", StringComparison.OrdinalIgnoreCase))
            .Select(u => new Update(
                (string?)u.Element("Id") ?? "",
                ((string?)u.Element("Name") ?? "System BIOS").Trim(),
                ((string?)u.Element("Version") ?? "").Trim(),
                DateTime.TryParseExact((string?)u.Element("DateReleased"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null,
                Https((string?)u.Element("Url")),
                Https((string?)u.Element("ReleaseNotesUrl")),
                long.TryParse((string?)u.Element("Size"), NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null,
                ((string?)u.Element("Description") ?? "").Trim(),
                ((string?)u.Descendants("BiosFamily").FirstOrDefault() ?? "").Trim(),
                ((string?)u.Element("ReleaseType") ?? "").Trim()))
            .ToList();
    }


    [GeneratedRegex(@"^\s*(\w+)\s+Ver\.?\s*([\d.]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex InstalledRegex();
}
