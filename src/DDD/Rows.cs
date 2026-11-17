using System.Globalization;
using Avalonia.Media;
using DDD.Core;

namespace DDD;

/// <summary>A finding as the Updates grid shows it.</summary>
public sealed class FindingRow(Finding finding, DDD.Core.Install.DownloadItem? download = null) : System.ComponentModel.INotifyPropertyChanged
{
    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The download of this update, once one was started.</summary>
    public DDD.Core.Install.DownloadItem? Download { get; } = download;

    public string DownloadText => Download?.StatusText ?? "";

    public IBrush DownloadBrush => Download?.State switch
    {
        DDD.Core.Install.DownloadState.Failed => FailBrush,
        DDD.Core.Install.DownloadState.Installed or DDD.Core.Install.DownloadState.RestartNeeded => OkBrush,
        DDD.Core.Install.DownloadState.Ready => ReadyBrush,
        _ => ManualBrush,
    };

    private static readonly IBrush FailBrush = new SolidColorBrush(Color.Parse("#FFE06060"));
    private static readonly IBrush ReadyBrush = new SolidColorBrush(Color.Parse("#FF4FA8F5"));

    /// <summary>Tells the grid the download status changed.</summary>
    public void NotifyDownload()
    {
        PropertyChanged?.Invoke(this, new(nameof(DownloadText)));
        PropertyChanged?.Invoke(this, new(nameof(DownloadBrush)));
    }

    private static readonly IBrush UpdateBrush = new SolidColorBrush(Color.Parse("#FFE0A040"));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#FF5DB85D"));
    private static readonly IBrush ManualBrush = new SolidColorBrush(Color.Parse("#FF7FA3D6"));

    public Finding Finding { get; } = finding;

    public string StatusText => Finding.Status switch
    {
        FindingStatus.UpdateAvailable => "Update available",
        FindingStatus.UpToDate => "Up to date",
        _ => "Check manually",
    };

    public IBrush StatusBrush => Finding.Status switch
    {
        FindingStatus.UpdateAvailable => UpdateBrush,
        FindingStatus.UpToDate => OkBrush,
        _ => ManualBrush,
    };

    /// <summary>Sort key: updates first, then manual checks, then up to date.</summary>
    public int StatusOrder => Finding.Status switch
    {
        FindingStatus.UpdateAvailable => 0,
        FindingStatus.CheckManually => 1,
        _ => 2,
    };

    public string Device => Finding.Device;
    public string Category => Finding.Kind == UpdateKind.Bios ? "BIOS" : Pretty(Finding.Category);
    public string Installed => Finding.InstalledVersion;
    public string Available => Finding.AvailableVersion;
    public string Released => Date(Finding.AvailableDate);
    public string Source => Finding.Source;

    public bool Matches(string filter) =>
        filter.Length == 0
        || Device.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Source.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Finding.Provider.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Finding.Title.Contains(filter, StringComparison.OrdinalIgnoreCase);

    internal static string Date(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "";

    /// <summary>Windows class names are upper case ("DISPLAY", "NET"); show them as words.</summary>
    internal static string Pretty(string deviceClass) => deviceClass.ToUpperInvariant() switch
    {
        "" => "",
        "NET" => "Network",
        "HDC" => "Storage controller",
        "SCSIADAPTER" => "Storage controller",
        "MEDIA" => "Sound",
        "HIDCLASS" => "Input",
        "USB" => "USB",
        "OTHERHARDWARE" => "Other",
        "BLUETOOTH" => "Bluetooth",
        "SOFTWARECOMPONENT" => "Software component",
        "SOFTWAREDEVICE" => "Software device",
        "DISKDRIVE" => "Disk",
        "AUDIOENDPOINT" => "Audio endpoint",
        "PRINTQUEUE" => "Printer",
        _ => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(deviceClass.ToLowerInvariant()),
    };
}

/// <summary>A device as the Devices grid shows it.</summary>
public sealed class DeviceRow(DeviceInfo device)
{
    public DeviceInfo Device { get; } = device;
    public string Name => Device.Name;
    public string Category => FindingRow.Pretty(Device.DeviceClass);
    public string Provider => Device.Provider;
    public string Version => Device.VersionText;
    public string Date => FindingRow.Date(Device.DriverDate);
    public string Inf => Device.InfName;
    public string HardwareId => Device.HardwareIds.Count > 0 ? Device.HardwareIds[0] : "";
    public string Problem => Device.HasProblem ? "Problem" : "";

    public bool Matches(string filter) =>
        filter.Length == 0
        || Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Provider.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || HardwareId.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Inf.Contains(filter, StringComparison.OrdinalIgnoreCase);
}
