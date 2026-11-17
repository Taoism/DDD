using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace DDD.Core.Sources;

/// <summary>
/// Asks Windows Update (through the Windows Update Agent COM API, the same one Settings uses)
/// which driver updates it has for this PC, including optional ones.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsUpdateSource : IUpdateSource
{
    public const string SourceName = "Windows Update";

    public string Name => SourceName;

    /// <summary>A driver update as the Windows Update Agent describes it.</summary>
    internal sealed record WuDriver(
        string UpdateId,
        string Title,
        string Description,
        string SupportUrl,
        string HardwareId,
        DateTime? VersionDate,
        string Provider,
        string DriverClass,
        string Model,
        bool Optional,
        long? MaxSize,
        string Severity);

    public async Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct)
    {
        context.Report(Name, "Searching Windows Update (this takes up to a minute)…");
        var drivers = await Task.Factory.StartNew(Search, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).ConfigureAwait(false);
        context.Report(Name, $"Windows Update offers {drivers.Count} driver update(s)");

        var findings = new List<Finding>();
        foreach (var driver in drivers)
        {
            ct.ThrowIfCancellationRequested();
            var device = context.Devices.FirstOrDefault(d => d.Matches(driver.HardwareId));
            IReadOnlyList<string> urls = [];
            try
            {
                urls = await CatalogClient.GetDownloadUrlsAsync(driver.UpdateId, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // Windows Update can still install it; the link is a bonus
            }
            findings.Add(ToFinding(driver, device, urls));
        }
        return findings;
    }

    internal static Finding ToFinding(WuDriver driver, DeviceInfo? device, IReadOnlyList<string> urls)
    {
        var notes = new StringBuilder();
        if (driver.Description.Length > 0 && !driver.Description.Equals(driver.Title, StringComparison.OrdinalIgnoreCase))
            notes.AppendLine(driver.Description).AppendLine();
        notes.Append(driver.Optional
            ? "Windows Update offers this as an optional driver update (Settings › Windows Update › Advanced options › Optional updates)."
            : "Windows Update will install this driver automatically.");
        if (device == null)
            notes.AppendLine().AppendLine().Append("No device on this PC has hardware ID ").Append(driver.HardwareId)
                .Append(" yet; Windows Update offers it as an add-on component.");

        return new Finding
        {
            Kind = UpdateKind.Driver,
            Status = FindingStatus.UpdateAvailable,
            Source = SourceName,
            Device = device?.Name ?? (driver.Model.Length > 0 ? driver.Model : driver.Title),
            Category = device?.DeviceClass ?? driver.DriverClass,
            DeviceInstanceId = device?.InstanceId,
            InstalledVersion = device?.VersionText ?? "not installed",
            InstalledDate = device?.DriverDate,
            AvailableVersion = DriverVersion.FindIn(driver.Title)?.ToString() ?? "",
            AvailableDate = driver.VersionDate,
            Title = driver.Title,
            Provider = driver.Provider,
            Notes = notes.ToString(),
            DownloadUrl = urls.Count > 0 ? urls[0] : null,
            MoreDownloadUrls = urls.Skip(1).ToList(),
            InfoUrl = CatalogClient.DetailsUrl(driver.UpdateId),
            ReleaseNotesUrl = CatalogSource.UsefulLink(driver.SupportUrl.Length > 0 ? driver.SupportUrl : null),
            Size = driver.MaxSize,
            Severity = driver.Severity.Length > 0 ? driver.Severity : (driver.Optional ? "Optional" : "Recommended"),
        };
    }

    private static List<WuDriver> Search()
    {
        var type = Type.GetTypeFromProgID("Microsoft.Update.Session")
            ?? throw new InvalidOperationException("The Windows Update Agent is not available on this PC.");
        dynamic session = Activator.CreateInstance(type)!;
        dynamic? searcher = null;
        dynamic? result = null;
        try
        {
            session.ClientApplicationID = "DDD";
            searcher = session.CreateUpdateSearcher();
            searcher.ServerSelection = 2; // ssWindowsUpdate: Microsoft's servers, even when WSUS manages the PC
            searcher.Online = true;
            result = searcher.Search("IsInstalled=0 and Type='Driver'");
            var list = new List<WuDriver>();
            int count = result.Updates.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic u = result.Updates.Item(i);
                try
                {
                    list.Add(new WuDriver(
                        (string)u.Identity.UpdateID,
                        (string)(u.Title ?? ""),
                        (string)(u.Description ?? ""),
                        (string)(u.SupportUrl ?? ""),
                        (string)(u.DriverHardwareID ?? ""),
                        u.DriverVerDate is DateTime date ? date.Date : (DateTime?)null,
                        (string)(u.DriverProvider ?? ""),
                        (string)(u.DriverClass ?? ""),
                        (string)(u.DriverModel ?? ""),
                        (bool)u.BrowseOnly,
                        ToLong(u.MaxDownloadSize),
                        (string)(u.MsrcSeverity ?? "")));
                }
                finally
                {
                    Marshal.FinalReleaseComObject(u);
                }
            }
            return list;
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException($"Windows Update search failed (0x{ex.HResult:X8}). Is the Windows Update service disabled?", ex);
        }
        finally
        {
            if (result != null)
                Marshal.FinalReleaseComObject(result);
            if (searcher != null)
                Marshal.FinalReleaseComObject(searcher);
            Marshal.FinalReleaseComObject(session);
        }
    }

    private static long? ToLong(object? value)
    {
        try
        {
            return value == null ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return null;
        }
    }
}
