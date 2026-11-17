namespace DDD.Core.Install;

public enum DownloadState
{
    None,
    Queued,
    Downloading,
    Extracting,

    /// <summary>Downloaded and checked; ready to install.</summary>
    Ready,

    Installing,
    Installed,

    /// <summary>Installed; Windows needs a restart to finish.</summary>
    RestartNeeded,

    /// <summary>Downloaded, but DDD doesn't install this kind (BIOS, unknown package): open the folder.</summary>
    Manual,

    Failed,
}

/// <summary>One update's download: where it is, how far it got, and what happened.</summary>
public sealed class DownloadItem(Finding finding)
{
    public Finding Finding { get; } = finding;

    public DownloadState State { get; private set; }

    /// <summary>0–100 while downloading, -1 when the size is unknown.</summary>
    public int Percent { get; private set; }

    public string Message { get; private set; } = "";

    /// <summary>The downloaded file.</summary>
    public string? FilePath { get; internal set; }

    /// <summary>Folder with the extracted driver (.inf) files of a .cab package.</summary>
    public string? DriverFolder { get; internal set; }

    /// <summary>The folder this update's files are in.</summary>
    public string? Folder { get; internal set; }

    /// <summary>Who signed the installer (.exe), when it is signed.</summary>
    public string Signer { get; internal set; } = "";

    public bool IsCab => DriverFolder != null;

    public bool CanInstall => State == DownloadState.Ready && Finding.Kind != UpdateKind.Bios;

    /// <summary>Raised on any change, from whatever thread made it.</summary>
    public event EventHandler? Changed;

    public void Set(DownloadState state, string message = "", int percent = 0)
    {
        State = state;
        Message = message;
        Percent = percent;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Short text for a grid cell.</summary>
    public string StatusText => State switch
    {
        DownloadState.None => "",
        DownloadState.Queued => "Queued",
        DownloadState.Downloading => Percent >= 0 ? $"Downloading {Percent}%" : "Downloading…",
        DownloadState.Extracting => "Extracting…",
        DownloadState.Ready => "Ready to install",
        DownloadState.Installing => "Installing…",
        DownloadState.Installed => "Installed",
        DownloadState.RestartNeeded => "Installed – restart",
        DownloadState.Manual => "Downloaded",
        _ => "Failed",
    };
}
