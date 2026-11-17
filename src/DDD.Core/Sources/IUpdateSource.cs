namespace DDD.Core.Sources;

/// <summary>What to check and how.</summary>
public sealed record ScanOptions
{
    public bool WindowsUpdate { get; init; } = true;
    public bool Catalog { get; init; } = true;
    public bool Nvidia { get; init; } = true;
    public bool Bios { get; init; } = true;

    /// <summary>Drivers from the PC maker's catalog (Dell, HP, Lenovo).</summary>
    public bool OemDrivers { get; init; } = true;

    /// <summary>When sources disagree, recommend the device maker's driver over Windows Update's (instead of the highest version).</summary>
    public bool PreferMaker { get; init; } = true;

    /// <summary>Also look up vendor drivers for devices that use Microsoft's built-in drivers.</summary>
    public bool IncludeMicrosoftDrivers { get; init; }

    /// <summary>NVIDIA Studio driver instead of Game Ready.</summary>
    public bool NvidiaStudio { get; init; }

    /// <summary>Parallel catalog lookups.</summary>
    public int Concurrency { get; init; } = 4;
}

/// <summary>Everything a source needs: the PC, its devices, options and where to report progress.</summary>
public sealed record ScanContext(SystemInfo System, IReadOnlyList<DeviceInfo> Devices, ScanOptions Options, IProgress<ScanProgress>? Progress)
{
    public void Report(string source, string message) => Progress?.Report(new ScanProgress(source, message));
}

/// <summary>A place DDD asks for newer drivers or BIOS versions.</summary>
public interface IUpdateSource
{
    string Name { get; }

    Task<IReadOnlyList<Finding>> CheckAsync(ScanContext context, CancellationToken ct);
}
