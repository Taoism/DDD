using System.Diagnostics;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DDD.Core;
using DDD.Core.Install;
using DDD.Dialogs;

namespace DDD;

/// <summary>Download all / Install: packages go to %TEMP%\DDD\downloads, get checked, then installed.</summary>
public sealed partial class MainWindow
{
    // by download URL, so a rescan keeps what was already downloaded
    private readonly Dictionary<string, DownloadItem> _downloads = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _downloadCts;
    private bool _installing;

    private DownloadItem? DownloadFor(Finding f) =>
        f.DownloadUrl != null && _downloads.TryGetValue(f.DownloadUrl, out var item) ? item : null;

    private IEnumerable<FindingRow> Rows => FindingsGrid.ItemsSource?.OfType<FindingRow>() ?? [];

    /// <summary>Updates in the list that can be downloaded and aren't yet.</summary>
    private List<Finding> Downloadable() =>
        (_result?.Findings ?? []).Where(Downloader.CanDownload)
            .Where(f => DownloadFor(f) is not { State: DownloadState.Ready or DownloadState.Installed or DownloadState.RestartNeeded or DownloadState.Manual or DownloadState.Downloading or DownloadState.Queued or DownloadState.Extracting })
            .ToList();

    private List<DownloadItem> Installable() => _downloads.Values.Where(d => d.CanInstall).ToList();

    private void UpdateDownloadButtons()
    {
        int toDownload = Downloadable().Count;
        int toInstall = Installable().Count;
        bool busy = _downloadCts != null || _installing;
        DownloadAllButton.IsEnabled = !busy && toDownload > 0;
        DownloadAllText.Text = _downloadCts != null ? "Downloading…" : toDownload > 0 ? $"Download all ({toDownload})" : "Download all";
        InstallButton.IsEnabled = !busy && toInstall > 0;
        InstallText.Text = _installing ? "Installing…" : toInstall > 0 ? $"Install ({toInstall})" : "Install";
        OpenFolderButton.IsVisible = _downloads.Values.Any(d => d.Folder != null);
    }

    private DownloadItem Track(Finding f)
    {
        if (DownloadFor(f) is { } existing)
            return existing;
        var item = new DownloadItem(f);
        _downloads[f.DownloadUrl!] = item;
        item.Changed += (_, _) => Dispatcher.UIThread.Post(() => OnDownloadChanged(item));
        return item;
    }

    private void OnDownloadChanged(DownloadItem item)
    {
        foreach (var row in Rows.Where(r => ReferenceEquals(r.Download, item)))
            row.NotifyDownload();
        if (Selected?.DownloadUrl == item.Finding.DownloadUrl)
            ShowDownloadDetails(item.Finding);
        UpdateDownloadButtons();
    }

    // ---------- download ----------

    private async void OnDownloadAllClick(object? sender, RoutedEventArgs e) => await DownloadAsync(Downloadable());

    private async void OnDownloadClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } f && Downloader.CanDownload(f))
            await DownloadAsync([f]);
        else
            await OpenAsync(Selected?.DownloadUrl);
    }

    private async void OnBrowserDownloadClick(object? sender, RoutedEventArgs e) => await OpenAsync(Selected?.DownloadUrl);

    private async Task DownloadAsync(List<Finding> findings)
    {
        if (findings.Count == 0 || _downloadCts != null)
            return;
        if (_demo)
        {
            await MessageBox.ShowAsync(this, "This is the demo: there is nothing real to download. Run DDD without --demo.", MessageKind.Info, "DDD");
            return;
        }
        // several devices can share one package (an Intel chipset INF): download it once
        var items = findings.Select(Track).Distinct().ToList();
        RefreshFindings(); // rows made before these downloads existed don't know them yet
        foreach (var item in items)
            item.Set(DownloadState.Queued);

        _downloadCts = new CancellationTokenSource();
        UpdateDownloadButtons();
        DownloadStatusText.Text = $"Downloading {items.Count} package(s) to {Downloader.DefaultFolder}";
        try
        {
            using var gate = new SemaphoreSlim(2);
            await Task.WhenAll(items.Select(async item =>
            {
                await gate.WaitAsync(_downloadCts.Token);
                try
                {
                    await Task.Run(() => Downloader.RunAsync(item, Downloader.DefaultFolder, _downloadCts.Token), _downloadCts.Token);
                }
                finally
                {
                    gate.Release();
                }
            }));
        }
        catch (OperationCanceledException)
        {
            DownloadStatusText.Text = "Download cancelled.";
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
        }

        int ready = items.Count(i => i.State == DownloadState.Ready);
        int failed = items.Count(i => i.State == DownloadState.Failed);
        int manual = items.Count(i => i.State == DownloadState.Manual);
        DownloadStatusText.Text = $"{ready} ready to install"
            + (manual > 0 ? $" · {manual} to install by hand" : "")
            + (failed > 0 ? $" · {failed} failed (select a line to see why)" : "");
        UpdateDownloadButtons();
    }

    // ---------- install ----------

    private async void OnInstallClick(object? sender, RoutedEventArgs e) => await InstallAsync(Installable());

    private async void OnInstallOneClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } f && DownloadFor(f) is { CanInstall: true } item)
            await InstallAsync([item]);
    }

    private async Task InstallAsync(List<DownloadItem> items)
    {
        if (items.Count == 0 || _installing)
            return;
        if (_demo)
        {
            await MessageBox.ShowAsync(this, "This is the demo: nothing is installed. Run DDD without --demo.", MessageKind.Info, "DDD");
            return;
        }
        var drivers = items.Where(i => i.IsCab).ToList();
        var installers = items.Where(i => !i.IsCab).ToList();

        var text = new System.Text.StringBuilder();
        text.Append($"Install {items.Count} update(s)?\n\n");
        if (drivers.Count > 0)
        {
            text.Append($"{drivers.Count} driver package(s) are added with Windows' own pnputil. Windows asks once for administrator permission; " +
                        "DDD first tries to create a System Restore point.\n\n");
        }
        if (installers.Count > 0)
            text.Append($"{installers.Count} vendor installer(s) open one after another; follow their steps.\n\n");
        foreach (var item in items)
            text.Append("• ").Append(item.Finding.Device).Append("  ").Append(item.Finding.AvailableVersion).Append('\n');
        if (!await MessageBox.ConfirmAsync(this, text.ToString().TrimEnd(), "Install", "Install updates"))
            return;

        _installing = true;
        UpdateDownloadButtons();
        bool restart = false;
        try
        {
            if (drivers.Count > 0)
            {
                foreach (var d in drivers)
                    d.Set(DownloadState.Installing);
                DownloadStatusText.Text = "Installing driver packages (answer the administrator prompt)…";
                var results = await DriverInstaller.InstallDriversAsync(drivers, restorePoint: true, CancellationToken.None);
                if (results == null)
                {
                    foreach (var d in drivers)
                        d.Set(DownloadState.Ready, "Not installed: the administrator prompt was declined.");
                }
                else
                {
                    foreach (var (item, result) in results)
                    {
                        if (result.Installed)
                        {
                            restart |= result.RestartNeeded;
                            item.Set(result.RestartNeeded ? DownloadState.RestartNeeded : DownloadState.Installed,
                                "Installed with pnputil." + (result.RestartNeeded ? " Restart Windows to finish." : ""));
                        }
                        else if (result.AddedOnly)
                        {
                            item.Set(DownloadState.Installed,
                                "Added to the Windows driver store. Windows kept the current driver for now because it ranks it the same or better; " +
                                "Device Manager › Update driver › Browse › Let me pick can switch to it.");
                        }
                        else
                        {
                            item.Set(DownloadState.Failed, $"pnputil exit code {result.ExitCode}.\n{TextTools.Cut(result.Output, 800)}");
                        }
                    }
                }
            }

            foreach (var item in installers)
            {
                item.Set(DownloadState.Installing, "The vendor's installer is open.");
                DownloadStatusText.Text = $"Waiting for the {item.Finding.Device} installer to close…";
                int? code = await DriverInstaller.RunInstallerAsync(item.FilePath!, CancellationToken.None);
                if (code == null)
                    item.Set(DownloadState.Ready, "Not installed: the administrator prompt was declined.");
                else if (code is 0 or 3010 or 1641)
                    item.Set(code == 0 ? DownloadState.Installed : DownloadState.RestartNeeded, $"The installer finished (exit code {code}).");
                else
                    item.Set(DownloadState.Failed, $"The installer ended with exit code {code}. It may have been cancelled; you can run it again.");
                restart |= code is 3010 or 1641;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            foreach (var item in items.Where(i => i.State == DownloadState.Installing))
                item.Set(DownloadState.Failed, ex.Message);
        }
        finally
        {
            _installing = false;
        }

        int done = items.Count(i => i.State is DownloadState.Installed or DownloadState.RestartNeeded);
        DownloadStatusText.Text = $"{done} of {items.Count} installed" + (restart ? " · restart Windows to finish" : "") + " · scan again to confirm";
        UpdateDownloadButtons();
    }

    // ---------- folders ----------

    private void OnOpenDownloadFolderClick(object? sender, RoutedEventArgs e) => OpenFolder(Downloader.DefaultFolder);

    private void OnOpenItemFolderClick(object? sender, RoutedEventArgs e)
    {
        if (Selected is { } f && DownloadFor(f)?.Folder is { } folder)
            OpenFolder(folder);
    }

    private void OpenFolder(string folder)
    {
        if (!Directory.Exists(folder))
            return;
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
            {
                ArgumentList = { folder },
                UseShellExecute = false,
            })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StatusText.Text = "Could not open the folder: " + ex.Message;
        }
    }

    /// <summary>Download state of the selected update in the details pane.</summary>
    private void ShowDownloadDetails(Finding f)
    {
        var item = DownloadFor(f);
        bool canDownload = Downloader.CanDownload(f);
        DownloadButton.IsVisible = canDownload || f.DownloadUrl != null;
        DownloadButton.IsEnabled = item is not { State: DownloadState.Downloading or DownloadState.Queued or DownloadState.Extracting or DownloadState.Installing };
        InstallOneButton.IsVisible = item is { CanInstall: true } && !_installing;
        FolderOneButton.IsVisible = item?.Folder != null;
        string message = item == null ? "" : item.StatusText + (item.Message.Length > 0 ? " – " + item.Message : "");
        DownloadMessage.Text = message;
        DownloadMessage.IsVisible = message.Length > 0;
        DownloadMessage.Foreground = new FindingRow(f, item).DownloadBrush;
    }
}
