using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DDD.Core;

/// <summary>Scan results as JSON or as a self-contained HTML page.</summary>
public static class Reports
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string ToJson(ScanResult result) => JsonSerializer.Serialize(result, JsonOptions);

    public static string ToHtml(ScanResult result, bool includeUpToDate)
    {
        static string E(string? s) => WebUtility.HtmlEncode(s ?? "");
        static string D(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";
        static string Link(string? url, string text) =>
            url == null ? "" : $"<a href=\"{E(url)}\" target=\"_blank\" rel=\"noopener\">{E(text)}</a>";

        var s = result.System;
        var findings = result.Findings.Where(f => includeUpToDate || f.Status != FindingStatus.UpToDate).ToList();
        var sb = new StringBuilder();
        sb.Append("""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
            <title>DDD driver report</title>
            <style>
            :root { --bg:#1b1c1f; --panel:#222428; --edge:#3a3c42; --text:#e8e8ea; --muted:#9a9ca1; --accent:#4a8ce3; --ok:#6cbf6c; --upd:#e0a040; --man:#8fa7c9; }
            @media (prefers-color-scheme: light) { :root { --bg:#f1f2f4; --panel:#fff; --edge:#d4d6db; --text:#1d1e21; --muted:#62646b; --accent:#2f6fc4; --ok:#2e8b3e; --upd:#b56d00; --man:#45628c; } }
            body { margin:0; padding:24px 16px; background:var(--bg); color:var(--text); font:14px/1.45 "Segoe UI",system-ui,sans-serif; }
            main { max-width:1100px; margin:0 auto; }
            h1 { margin:0 0 4px; font-size:22px; } .muted { color:var(--muted); }
            .card { background:var(--panel); border:1px solid var(--edge); border-radius:6px; padding:12px 14px; margin:12px 0; }
            .head { display:flex; flex-wrap:wrap; gap:6px 16px; align-items:baseline; }
            .name { font-weight:600; font-size:15px; } .ver { font-family:Consolas,monospace; }
            .tag { font-size:12px; padding:1px 8px; border-radius:10px; border:1px solid currentColor; }
            .UpdateAvailable { color:var(--upd); } .UpToDate { color:var(--ok); } .CheckManually { color:var(--man); }
            pre { white-space:pre-wrap; word-wrap:break-word; font:13px/1.45 "Segoe UI",system-ui,sans-serif; margin:8px 0 0; }
            a { color:var(--accent); } .links a { margin-right:14px; }
            </style></head><body><main>
            """);
        sb.Append("<h1>DDD — Drivers, Drivers, Drivers</h1>");
        sb.Append(CultureInfo.InvariantCulture,
            $"<div class=\"muted\">{E(s.DisplayName)} · BIOS {E(s.BiosVersion)} ({D(s.BiosDate)}) · {E(s.OsName)} build {E(s.OsBuildText)} · scanned {result.Finished:yyyy-MM-dd HH:mm}</div>");
        int updates = findings.Count(f => f.Status == FindingStatus.UpdateAvailable);
        sb.Append(CultureInfo.InvariantCulture, $"<p>{updates} update(s) found among {result.Devices.Count} devices.</p>");
        foreach (var m in result.Messages)
            sb.Append(CultureInfo.InvariantCulture, $"<p class=\"muted\">{E(m.Source)}: {E(m.Message)}</p>");

        foreach (var f in findings)
        {
            string status = f.Status switch
            {
                FindingStatus.UpdateAvailable => "Update available",
                FindingStatus.UpToDate => "Up to date",
                _ => "Check manually",
            };
            sb.Append("<div class=\"card\"><div class=\"head\">");
            sb.Append(CultureInfo.InvariantCulture, $"<span class=\"name\">{E(f.Device)}</span><span class=\"tag {f.Status}\">{status}</span>");
            sb.Append(CultureInfo.InvariantCulture, $"<span class=\"muted\">{E(f.Category)} · {E(f.Source)}</span></div>");
            sb.Append(CultureInfo.InvariantCulture,
                $"<div>Installed <span class=\"ver\">{E(f.InstalledVersion)}</span> {D(f.InstalledDate)}");
            if (f.AvailableVersion.Length > 0)
                sb.Append(CultureInfo.InvariantCulture, $" → available <span class=\"ver\">{E(f.AvailableVersion)}</span> {D(f.AvailableDate)}");
            sb.Append("</div>");
            if (f.Title.Length > 0)
                sb.Append(CultureInfo.InvariantCulture, $"<div class=\"muted\">{E(f.Title)}</div>");
            if (f.Notes.Length > 0)
                sb.Append(CultureInfo.InvariantCulture, $"<pre>{E(f.Notes)}</pre>");
            sb.Append("<div class=\"links\">");
            sb.Append(Link(f.DownloadUrl, "Download")).Append(Link(f.InfoUrl, "Details")).Append(Link(f.ReleaseNotesUrl, "Release notes"));
            sb.Append("</div></div>");
        }
        sb.Append("</main></body></html>");
        return sb.ToString();
    }
}
