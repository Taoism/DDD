using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;

namespace DDD.Core.Install;

/// <summary>
/// Installs downloaded updates. Driver packages (.cab, extracted) are added with Windows' own
/// pnputil in one elevated script, so there is a single administrator prompt; it first tries to
/// make a System Restore point. Vendor installers (.exe) are started one after the other.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class DriverInstaller
{
    private const int ErrorCancelled = 1223;

    /// <summary>pnputil's result for one driver folder.</summary>
    public sealed record PnpResult(int ExitCode, string Output)
    {
        /// <summary>0 = installed, 3010 = installed and Windows wants a restart.</summary>
        public bool Installed => ExitCode is 0 or 3010;

        public bool RestartNeeded => ExitCode == 3010 || Output.Contains("reboot", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// 259 (ERROR_NO_MORE_ITEMS): the package was added to the driver store, but Windows kept the
        /// current driver because it ranks the same or better for the device.
        /// </summary>
        public bool AddedOnly => ExitCode == 259;
    }

    /// <summary>
    /// Adds and installs every driver folder with pnputil in one elevated run. Returns null when the
    /// user declined the administrator prompt, otherwise the result per item.
    /// </summary>
    public static async Task<IReadOnlyDictionary<DownloadItem, PnpResult>?> InstallDriversAsync(
        IReadOnlyList<DownloadItem> items, bool restorePoint, CancellationToken ct)
    {
        string work = Path.Combine(Downloader.DefaultFolder, "install-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(work);
        var logs = new Dictionary<DownloadItem, string>();
        for (int i = 0; i < items.Count; i++)
            logs[items[i]] = Path.Combine(work, $"driver{i + 1}.log");

        string script = Path.Combine(work, "install.cmd");
        await File.WriteAllTextAsync(script, BuildScript(items.Select(i => (i.DriverFolder!, logs[i])).ToList(), restorePoint, work), Encoding.ASCII, ct).ConfigureAwait(false);

        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = $"/d /c \"\"{script}\"\"",
            WorkingDirectory = work,
        };
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
        if (process == null)
            throw new InvalidOperationException("The installer script did not start.");
        using (process)
            await process.WaitForExitAsync(ct).ConfigureAwait(false);

        var results = new Dictionary<DownloadItem, PnpResult>();
        foreach (var (item, log) in logs)
        {
            string text = File.Exists(log) ? await File.ReadAllTextAsync(log, ct).ConfigureAwait(false) : "";
            results[item] = ParseLog(text);
        }
        return results;
    }

    /// <summary>The elevated cmd script: optional restore point, then pnputil per driver folder, each logging its exit code.</summary>
    internal static string BuildScript(IReadOnlyList<(string Folder, string Log)> drivers, bool restorePoint, string work)
    {
        string pnputil = Path.Combine(Environment.SystemDirectory, "pnputil.exe");
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        if (restorePoint)
        {
            // Windows allows one restore point per 24 hours by default; a refusal is not an error here
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"\"{powershell}\" -NoProfile -NonInteractive -Command \"Checkpoint-Computer -Description 'DDD driver install' -RestorePointType MODIFY_SETTINGS\" > \"{Path.Combine(work, "restore-point.log")}\" 2>&1");
        }
        foreach (var (folder, log) in drivers)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"\"{pnputil}\" /add-driver \"{Path.Combine(folder, "*.inf")}\" /subdirs /install > \"{log}\" 2>&1");
            sb.AppendLine(CultureInfo.InvariantCulture, $"echo DDD-EXIT %errorlevel% >> \"{log}\"");
        }
        return sb.ToString();
    }

    internal static PnpResult ParseLog(string text)
    {
        var m = ExitRegex().Match(text);
        int code = m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int c) ? c : -1;
        string output = ExitRegex().Replace(text, "").Trim();
        return new PnpResult(code, output);
    }

    /// <summary>Starts a vendor installer and waits for it to close. Null when the user declined its administrator prompt.</summary>
    public static async Task<int?> RunInstallerAsync(string exe, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
        if (process == null)
            return -1;
        using (process)
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    [GeneratedRegex(@"DDD-EXIT\s+(-?\d+)", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ExitRegex();
}
