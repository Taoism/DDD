using System.Globalization;
using System.Text;
using DDD.Core;
using DDD.Core.Sources;

namespace DDD.Cli;

internal static class Program
{
    private const string Usage = """
        ddd-cli - Drivers, Drivers, Drivers: finds newer drivers and BIOS for this PC.

        Usage:
          ddd-cli scan [options]    check for updates (default)
          ddd-cli devices           list devices and their installed drivers
          ddd-cli system            show the PC's model, board, BIOS and Windows build
          ddd-cli model MAKER ID    list what Dell/HP/Lenovo publish for a model: MAKER is dell, hp or lenovo;
                                    ID is the Dell system SKU, HP platform ID or Lenovo machine type

        Scan options:
          --json                 print the full result as JSON
          --html FILE            also write an HTML report to FILE
          --all                  also list devices that are up to date
          --no-wu                skip Windows Update
          --no-catalog           skip the Microsoft Update Catalog
          --no-nvidia            skip NVIDIA
          --no-bios              skip the BIOS check
          --no-oem               skip the PC maker's driver catalog (Dell, HP, Lenovo)
          --no-prefer            pick the highest version instead of the device maker's driver
          --ms-drivers           also look up vendor drivers for devices on Microsoft's built-in drivers
          --studio               NVIDIA Studio driver instead of Game Ready
          --fresh                ignore catalog searches cached in the last 6 hours
        """;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        string command = args.Length > 0 && !args[0].StartsWith('-') ? args[0].ToLowerInvariant() : "scan";
        var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).Select(a => a.ToLowerInvariant()).ToHashSet();
        if (flags.Contains("--help") || command is "help" or "-h" or "/?")
        {
            Console.WriteLine(Usage);
            return 0;
        }

        try
        {
            switch (command)
            {
                case "system":
                    PrintSystem(SystemInventory.GetSystemInfo());
                    return 0;
                case "devices":
                    PrintDevices(SystemInventory.GetDevices());
                    return 0;
                case "model" when args.Length >= 3:
                    return await ModelAsync(args[1], args[2]);
                case "scan":
                    return await ScanAsync(args, flags);
                default:
                    await Console.Error.WriteLineAsync($"Unknown command '{command}'.\n");
                    await Console.Error.WriteLineAsync(Usage);
                    return 2;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            await Console.Error.WriteLineAsync("Error: " + ex.Message);
            return 1;
        }
    }

    private static async Task<int> ScanAsync(string[] args, HashSet<string> flags)
    {
        var options = new ScanOptions
        {
            WindowsUpdate = !flags.Contains("--no-wu"),
            Catalog = !flags.Contains("--no-catalog"),
            Nvidia = !flags.Contains("--no-nvidia"),
            Bios = !flags.Contains("--no-bios"),
            OemDrivers = !flags.Contains("--no-oem"),
            PreferMaker = !flags.Contains("--no-prefer"),
            IncludeMicrosoftDrivers = flags.Contains("--ms-drivers"),
            NvidiaStudio = flags.Contains("--studio"),
        };
        if (flags.Contains("--fresh"))
            CatalogClient.ClearCache(CatalogClient.DefaultCacheFolder);
        bool json = flags.Contains("--json");
        int htmlIndex = Array.FindIndex(args, a => a.Equals("--html", StringComparison.OrdinalIgnoreCase));
        string? htmlPath = htmlIndex >= 0 && htmlIndex + 1 < args.Length ? args[htmlIndex + 1] : null;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cts.Cancel();
        };
        var progress = new SyncProgress(p =>
        {
            if (!json)
                Console.Error.WriteLine($"  [{p.Source}] {p.Message}");
        });
        var result = await Scanner.ScanAsync(options, progress, cts.Token);

        if (htmlPath != null)
            await File.WriteAllTextAsync(htmlPath, Reports.ToHtml(result, flags.Contains("--all")), cts.Token);

        if (json)
        {
            Console.WriteLine(Reports.ToJson(result));
            return 0;
        }

        Console.WriteLine();
        PrintSystem(result.System);
        Console.WriteLine();
        var shown = result.Findings.Where(f => flags.Contains("--all") || f.Status != FindingStatus.UpToDate).ToList();
        int updates = shown.Count(f => f.Status == FindingStatus.UpdateAvailable);
        Console.WriteLine(updates == 0 ? "No newer drivers found." : $"{updates} update(s) found:");
        foreach (var f in shown)
        {
            Console.WriteLine();
            string status = f.Status switch
            {
                FindingStatus.UpdateAvailable => "UPDATE",
                FindingStatus.UpToDate => "ok",
                _ => "CHECK",
            };
            Console.WriteLine($"[{status}] {f.Device}  ({f.Category}, {f.Source})");
            Console.WriteLine($"   installed {f.InstalledVersion} {Date(f.InstalledDate)}" +
                (f.AvailableVersion.Length > 0 ? $"  ->  available {f.AvailableVersion} {Date(f.AvailableDate)}" : ""));
            if (f.Status != FindingStatus.UpToDate)
            {
                foreach (string line in TextTools.Cut(f.Notes, 700).Split('\n'))
                    Console.WriteLine("   | " + line);
            }
            if (f.DownloadUrl != null)
                Console.WriteLine("   download: " + f.DownloadUrl);
            if (f.InfoUrl != null)
                Console.WriteLine("   details:  " + f.InfoUrl);
            if (f.ReleaseNotesUrl != null)
                Console.WriteLine("   notes:    " + f.ReleaseNotesUrl);
        }
        foreach (var m in result.Messages)
            await Console.Error.WriteLineAsync($"\n{(m.IsError ? "Error" : "Note")} from {m.Source}: {m.Message}");
        return result.Messages.Any(m => m.IsError) ? 3 : 0;
    }

    private static async Task<int> ModelAsync(string maker, string id)
    {
        var system = new SystemInfo
        {
            Manufacturer = maker,
            Model = id,
            Sku = id.ToUpperInvariant(),
            BoardProduct = id.ToLowerInvariant(),
            OsBuild = Environment.OSVersion.Version.Build,
        };
        var catalogs = new DDD.Core.Oem.OemCatalogs(system);
        if (catalogs.Maker.Length == 0)
        {
            await Console.Error.WriteLineAsync("MAKER must be dell, hp or lenovo.");
            return 2;
        }
        var context = new ScanContext(system, [], new ScanOptions(), null);
        var bios = await new DDD.Core.Bios.BiosSource(catalogs).CheckAsync(context, CancellationToken.None);
        foreach (var f in bios)
            Console.WriteLine($"BIOS: {f.Title} {f.AvailableVersion} {Date(f.AvailableDate)}  {f.DownloadUrl ?? f.InfoUrl}");
        var packages = await new DDD.Core.Oem.OemDriverSource(catalogs).PackagesAsync(4, CancellationToken.None);
        Console.WriteLine($"\n{packages.Count} driver packages:");
        foreach (var p in packages.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  {p.Name}  {p.Version}  {Date(p.Released)}  ({p.HardwareIds.Count} IDs, e.g. {p.HardwareIds[0]})");
            Console.WriteLine($"     {p.DownloadUrl}");
        }
        return 0;
    }

    private static void PrintSystem(SystemInfo s)
    {
        Console.WriteLine($"PC:       {s.Manufacturer} {s.Model}");
        Console.WriteLine($"Board:    {s.BoardManufacturer} {s.BoardProduct}");
        Console.WriteLine($"BIOS:     {s.BiosVersion} {Date(s.BiosDate)} ({s.BiosVendor})");
        Console.WriteLine($"Windows:  {s.OsName} build {s.OsBuildText}");
    }

    private static void PrintDevices(IReadOnlyList<DeviceInfo> devices)
    {
        foreach (var d in devices)
        {
            Console.WriteLine($"{d.DeviceClass,-16} {d.Name}");
            Console.WriteLine($"{"",-16} {d.VersionText} {Date(d.DriverDate)}  {d.Provider}  {d.InfName}  {(d.HardwareIds.Count > 0 ? d.HardwareIds[0] : "")}");
        }
        Console.WriteLine($"\n{devices.Count} devices");
    }

    private static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Reports on the calling thread right away (Progress&lt;T&gt; would post to the thread pool and lose order).</summary>
    private sealed class SyncProgress(Action<ScanProgress> report) : IProgress<ScanProgress>
    {
        private readonly Lock _lock = new();

        public void Report(ScanProgress value)
        {
            lock (_lock)
                report(value);
        }
    }
}
