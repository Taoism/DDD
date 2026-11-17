using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using DDD.Core;
using DDD.Core.Sources;
using DDD.Dialogs;

namespace DDD;

public sealed partial class MainWindow : Window
{
    public static WindowIcon AppIcon { get; } = new(AssetLoader.Open(new Uri("avares://DDD/Assets/ddd.ico")));

    private readonly Settings _settings;
    private readonly bool _scanOnOpen;
    private readonly bool _demo;
    private readonly bool _aboutOnOpen;
    private IReadOnlyList<DeviceInfo> _devices = [];
    private ScanResult? _result;
    private CancellationTokenSource? _scanCts;

    public MainWindow() : this(new Settings(), false, false, false)
    {
    }

    public MainWindow(Settings settings, bool scanOnOpen, bool demo, bool aboutOnOpen = false)
    {
        _settings = settings;
        _scanOnOpen = scanOnOpen;
        _demo = demo;
        _aboutOnOpen = aboutOnOpen;
        InitializeComponent();
        Icon = AppIcon;
        SignHost.Children.Add(Controls.NeonSign.Logo(24));

        WuCheck.IsChecked = settings.WindowsUpdate;
        CatalogCheck.IsChecked = settings.Catalog;
        NvidiaCheck.IsChecked = settings.Nvidia;
        BiosCheck.IsChecked = settings.Bios;
        OemCheck.IsChecked = settings.OemDrivers;
        PreferMakerCheck.IsChecked = settings.PreferMaker;
        MsDriversCheck.IsChecked = settings.IncludeMicrosoftDrivers;
        StudioCheck.IsChecked = settings.NvidiaStudio;
        ShowUpToDateCheck.IsChecked = settings.ShowUpToDate;
        UpdateThemeMenu();
        RestorePlacement();
    }

    // ---------- window lifetime ----------

    private void RestorePlacement()
    {
        if (_settings.Width is > 400 && _settings.Height is > 300)
        {
            Width = _settings.Width.Value;
            Height = _settings.Height.Value;
        }
        if (_settings.X is { } x && _settings.Y is { } y)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(x, y);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
        if (_settings.DetailWidth is > 250 and < 1200)
            BodyGrid.ColumnDefinitions[2].Width = new GridLength(_settings.DetailWidth.Value);
    }

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (_settings.Maximized)
            WindowState = WindowState.Maximized;
        if (_demo)
        {
            ShowResult(Demo.Result(), TimeSpan.FromSeconds(38));
            FindingsGrid.SelectedItem = FindingsGrid.ItemsSource?.OfType<FindingRow>()
                .FirstOrDefault(r => r.Source == "NVIDIA") ?? FindingsGrid.SelectedItem;
            if (_aboutOnOpen)
                await new AboutDialog().ShowDialog<bool>(this);
            return;
        }
        await LoadInventoryAsync();
        if (_scanOnOpen)
            await ScanAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _scanCts?.Cancel();
        if (!_demo)
            SaveSettings();
        base.OnClosing(e);
    }

    private void SaveSettings()
    {
        _settings.WindowsUpdate = WuCheck.IsChecked == true;
        _settings.Catalog = CatalogCheck.IsChecked == true;
        _settings.Nvidia = NvidiaCheck.IsChecked == true;
        _settings.Bios = BiosCheck.IsChecked == true;
        _settings.OemDrivers = OemCheck.IsChecked == true;
        _settings.PreferMaker = PreferMakerCheck.IsChecked == true;
        _settings.IncludeMicrosoftDrivers = MsDriversCheck.IsChecked == true;
        _settings.NvidiaStudio = StudioCheck.IsChecked == true;
        _settings.ShowUpToDate = ShowUpToDateCheck.IsChecked == true;
        _settings.Maximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.Width = Width;
            _settings.Height = Height;
            _settings.X = Position.X;
            _settings.Y = Position.Y;
        }
        _settings.DetailWidth = BodyGrid.ColumnDefinitions[2].ActualWidth;
        _settings.Save();
    }

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.F5 && _scanCts == null)
        {
            e.Handled = true;
            await ScanAsync();
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            e.Handled = true;
            FilterBox.Focus();
        }
    }

    // ---------- inventory and scan ----------

    private async Task LoadInventoryAsync()
    {
        try
        {
            var system = await Task.Run(SystemInventory.GetSystemInfo, CancellationToken.None);
            ShowSystem(system);
            _devices = await Task.Run(SystemInventory.GetDevices, CancellationToken.None);
            RefreshDevices();
            StatusText.Text = $"{_devices.Count} devices with drivers. Press Scan for updates (F5).";
        }
        catch (Exception ex) when (ex is System.Management.ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            StatusText.Text = "Could not read the device list: " + ex.Message;
        }
    }

    private void ShowSystem(SystemInfo s)
    {
        SystemText.Text = s.DisplayName;
        string biosDate = s.BiosDate is { } d ? " (" + d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ")" : "";
        SystemDetailText.Text = $"BIOS {s.BiosVersion}{biosDate} · {s.OsName.Replace("Microsoft ", "", StringComparison.Ordinal)} build {s.OsBuildText}";
    }

    private ScanOptions CurrentOptions() => new()
    {
        WindowsUpdate = WuCheck.IsChecked == true,
        Catalog = CatalogCheck.IsChecked == true,
        Nvidia = NvidiaCheck.IsChecked == true,
        Bios = BiosCheck.IsChecked == true,
        OemDrivers = OemCheck.IsChecked == true,
        PreferMaker = PreferMakerCheck.IsChecked == true,
        IncludeMicrosoftDrivers = MsDriversCheck.IsChecked == true,
        NvidiaStudio = StudioCheck.IsChecked == true,
    };

    private async void OnScanClick(object? sender, RoutedEventArgs e)
    {
        if (_scanCts != null)
        {
            await _scanCts.CancelAsync();
            return;
        }
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        var options = CurrentOptions();
        if (!options.WindowsUpdate && !options.Catalog && !options.Nvidia && !options.Bios && !options.OemDrivers)
        {
            await MessageBox.ShowAsync(this, "Tick at least one source to check (Windows Update, Update Catalog, NVIDIA, PC maker or BIOS).", MessageKind.Warning, "DDD");
            return;
        }

        _scanCts = new CancellationTokenSource();
        ScanButtonText.Text = "Cancel";
        Progress.IsVisible = true;
        MessagesText.Text = "";
        EmptyTitle.Text = "Scanning…";
        EmptyText.Text = "Windows Update can take up to a minute; catalog lookups are paced so Microsoft doesn't block them.";
        var progress = new Progress<ScanProgress>(p => StatusText.Text = $"{p.Source}: {p.Message}");
        var watch = Stopwatch.StartNew();
        try
        {
            ShowResult(await Scanner.ScanAsync(options, progress, _scanCts.Token), watch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Scan cancelled.";
            if (_result == null)
            {
                EmptyTitle.Text = "Scan cancelled";
                EmptyText.Text = "Press Scan for updates to try again.";
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or System.Management.ManagementException)
        {
            StatusText.Text = "Scan failed.";
            await MessageBox.ShowAsync(this, "The scan failed:\n\n" + ex.Message, MessageKind.Error, "DDD");
        }
        finally
        {
            _scanCts.Dispose();
            _scanCts = null;
            ScanButtonText.Text = "Scan for updates";
            Progress.IsVisible = false;
        }
    }

    private void ShowResult(ScanResult result, TimeSpan elapsed)
    {
        _result = result;
        _devices = _result.Devices;
        ShowSystem(_result.System);
        RefreshDevices();
        RefreshFindings();
        int updates = _result.Findings.Count(f => f.Status == FindingStatus.UpdateAvailable);
        int manual = _result.Findings.Count(f => f.Status == FindingStatus.CheckManually);
        int current = _result.Findings.Count(f => f.Status == FindingStatus.UpToDate);
        StatusText.Text = (updates == 0 ? "No newer drivers found" : $"{updates} update{(updates == 1 ? "" : "s")} found")
            + (manual > 0 ? $" · {manual} to check by hand" : "")
            + $" · {current} confirmed up to date · {_devices.Count} devices · {elapsed.TotalSeconds:0} s";
        MessagesText.Text = string.Join("   ", _result.Messages.Select(m => $"{m.Source}: {m.Message}"));
        ToolTip.SetTip(MessagesText, MessagesText.Text);
        Tabs.SelectedItem = UpdatesTab;
    }

    // ---------- grids ----------

    private void RefreshFindings()
    {
        if (_result == null)
            return;
        string filter = FilterBox.Text?.Trim() ?? "";
        bool showCurrent = ShowUpToDateCheck.IsChecked == true;
        var rows = _result.Findings
            .Where(f => showCurrent || f.Status != FindingStatus.UpToDate)
            .Select(f => new FindingRow(f))
            .Where(r => r.Matches(filter))
            .ToList();
        var selected = (FindingsGrid.SelectedItem as FindingRow)?.Finding;
        FindingsGrid.ItemsSource = new DataGridCollectionView(rows);
        int updates = _result.Findings.Count(f => f.Status == FindingStatus.UpdateAvailable);
        UpdatesTab.Header = updates > 0 ? $"Updates ({updates})" : "Updates";

        EmptyState.IsVisible = rows.Count == 0;
        if (rows.Count == 0)
        {
            EmptyTitle.Text = filter.Length > 0 ? "Nothing matches the filter" : "Everything checked is up to date";
            EmptyText.Text = filter.Length > 0
                ? "Clear the filter box to see all results."
                : "No source had anything newer than the installed drivers. Tick Show up-to-date to see what was compared.";
        }
        var again = selected == null ? null : rows.FirstOrDefault(r => r.Finding == selected);
        FindingsGrid.SelectedItem = again ?? rows.FirstOrDefault(r => r.Finding.Status != FindingStatus.UpToDate);
        if (FindingsGrid.SelectedItem == null)
            ShowDetails(null);
    }

    private void RefreshDevices()
    {
        string filter = FilterBox.Text?.Trim() ?? "";
        var rows = _devices.Select(d => new DeviceRow(d)).Where(r => r.Matches(filter)).ToList();
        DevicesGrid.ItemsSource = new DataGridCollectionView(rows);
        DevicesTab.Header = $"Devices ({_devices.Count})";
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        RefreshFindings();
        RefreshDevices();
    }

    private void OnShowUpToDateChanged(object? sender, RoutedEventArgs e) => RefreshFindings();

    private void OnFindingSelected(object? sender, SelectionChangedEventArgs e) => ShowDetails((FindingsGrid.SelectedItem as FindingRow)?.Finding);

    private Finding? Selected => (FindingsGrid.SelectedItem as FindingRow)?.Finding;

    private void ShowDetails(Finding? f)
    {
        DetailPane.IsVisible = f != null;
        DetailEmpty.IsVisible = f == null;
        if (f == null)
            return;

        var row = new FindingRow(f);
        DetailDevice.Text = f.Device;
        DetailDot.Fill = row.StatusBrush;
        DetailStatus.Text = row.StatusText;
        DetailStatus.Foreground = row.StatusBrush;
        DetailTitle.Text = f.Title;
        DetailTitle.IsVisible = f.Title.Length > 0 && !f.Title.Equals(f.Device, StringComparison.OrdinalIgnoreCase);

        FactInstalled.Text = Join(f.InstalledVersion, f.InstalledDate);
        FactAvailable.Text = f.AvailableVersion.Length > 0 ? Join(f.AvailableVersion, f.AvailableDate) : "—";
        FactSource.Text = f.Source;
        FactProvider.Text = f.Provider.Length > 0 ? f.Provider : "—";
        FactSize.Text = f.Size is { } size ? FormatSize(size) : "";
        FactSize.IsVisible = FactSizeLabel.IsVisible = f.Size != null;
        FactSeverity.Text = f.Severity;
        FactSeverity.IsVisible = FactSeverityLabel.IsVisible = f.Severity.Length > 0;

        DownloadButton.IsVisible = CopyButton.IsVisible = f.DownloadUrl != null;
        DetailsButton.IsVisible = f.InfoUrl != null;
        DetailsButtonText.Text = f.Status == FindingStatus.CheckManually ? "Open support page" : "Details page";
        if (f.Status == FindingStatus.CheckManually)
            DetailsButton.Classes.Add("accent");
        else
            DetailsButton.Classes.Remove("accent");
        NotesButton.IsVisible = f.ReleaseNotesUrl != null;
        WindowsUpdateButton.IsVisible = f.Source.Contains(WindowsUpdateSource.SourceName, StringComparison.Ordinal);

        NotesHeader.Text = f.Status switch
        {
            FindingStatus.UpToDate => "WHAT WAS COMPARED",
            FindingStatus.CheckManually => "WHAT TO DO",
            _ => "WHAT IT IMPROVES OR FIXES",
        };
        DetailNotes.Text = f.Notes.Length > 0 ? f.Notes : "The source gave no description for this update.";
        DetailPane.Offset = default;
    }

    private static string Join(string version, DateTime? date) =>
        date is { } d ? $"{version}   {d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}" : version;

    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => (bytes / (double)(1L << 30)).ToString("0.00", CultureInfo.InvariantCulture) + " GB",
        >= 1L << 20 => (bytes / (double)(1L << 20)).ToString("0.0", CultureInfo.InvariantCulture) + " MB",
        >= 1L << 10 => (bytes / 1024d).ToString("0", CultureInfo.InvariantCulture) + " KB",
        _ => bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
    };

    // ---------- actions ----------

    private async void OnDownloadClick(object? sender, RoutedEventArgs e) => await OpenAsync(Selected?.DownloadUrl);

    private async void OnDetailsClick(object? sender, RoutedEventArgs e) => await OpenAsync(Selected?.InfoUrl);

    private async void OnNotesClick(object? sender, RoutedEventArgs e) => await OpenAsync(Selected?.ReleaseNotesUrl);

    private async void OnCopyLinkClick(object? sender, RoutedEventArgs e)
    {
        string? url = Selected?.DownloadUrl ?? Selected?.InfoUrl;
        if (url == null || Clipboard == null)
            return;
        await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, url);
        StatusText.Text = "Link copied: " + url;
    }

    private async void OnCopyHardwareIdClick(object? sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is DeviceRow row && Clipboard != null)
        {
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, row.HardwareId);
            StatusText.Text = "Copied " + row.HardwareId;
        }
    }

    private async void OnSearchCatalogClick(object? sender, RoutedEventArgs e)
    {
        if (DevicesGrid.SelectedItem is DeviceRow row && row.Device.HardwareIds.Count > 0)
            await OpenAsync(CatalogClient.SearchUrl(CatalogSource.Query(row.Device)));
    }

    /// <summary>Opens a web link in the default browser; only http(s) links are ever opened.</summary>
    private async Task OpenAsync(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return;
        if (!await Launcher.LaunchUriAsync(uri))
            StatusText.Text = "Could not open " + url;
    }

    private async void OnOpenWindowsUpdate(object? sender, RoutedEventArgs e)
    {
        if (!await Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate-optionalupdates")))
            StatusText.Text = "Could not open Windows Update settings.";
    }

    private void OnOpenDeviceManager(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "devmgmt.msc")) { UseShellExecute = true })?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            StatusText.Text = "Could not open Device Manager: " + ex.Message;
        }
    }

    private void OnClearCacheClick(object? sender, RoutedEventArgs e)
    {
        CatalogClient.ClearCache(CatalogClient.DefaultCacheFolder);
        StatusText.Text = "Cached catalog searches removed; the next scan asks the catalog again.";
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (_result == null)
        {
            await MessageBox.ShowAsync(this, "Run a scan first; the report lists what it found.", MessageKind.Info, "DDD");
            return;
        }
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export report",
            SuggestedFileName = $"DDD report {_result.Finished:yyyy-MM-dd}",
            DefaultExtension = "html",
            FileTypeChoices =
            [
                new FilePickerFileType("Web page") { Patterns = ["*.html"] },
                new FilePickerFileType("JSON") { Patterns = ["*.json"] },
            ],
        });
        string? path = file?.TryGetLocalPath();
        if (path == null)
            return;
        try
        {
            string text = path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? Reports.ToJson(_result)
                : Reports.ToHtml(_result, ShowUpToDateCheck.IsChecked == true);
            await File.WriteAllTextAsync(path, text, CancellationToken.None);
            StatusText.Text = "Report saved: " + path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await MessageBox.ShowAsync(this, "Could not save the report:\n\n" + ex.Message, MessageKind.Error, "DDD");
        }
    }

    private void OnThemeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string theme })
        {
            _settings.Theme = theme;
            App.ApplyTheme(theme);
            UpdateThemeMenu();
            _settings.Save();
        }
    }

    private void UpdateThemeMenu()
    {
        ThemeDark.IsChecked = _settings.Theme == "Dark";
        ThemeLight.IsChecked = _settings.Theme == "Light";
        ThemeSystem.IsChecked = _settings.Theme is not ("Dark" or "Light");
    }

    private async void OnAboutClick(object? sender, RoutedEventArgs e) => await new AboutDialog().ShowDialog<bool>(this);
}
