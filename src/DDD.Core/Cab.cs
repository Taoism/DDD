using System.Diagnostics;
using System.Xml.Linq;

namespace DDD.Core;

/// <summary>Opens the single-file .cab catalogs Dell and HP publish, using Windows' own expand.exe.</summary>
public static class Cab
{
    /// <summary>Extracts the first file in <paramref name="cab"/> and returns its bytes.</summary>
    public static async Task<byte[]> ExtractFirstAsync(byte[] cab, CancellationToken ct)
    {
        string dir = Path.Combine(Path.GetTempPath(), "DDD-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string cabPath = Path.Combine(dir, "catalog.cab");
            string outDir = Path.Combine(dir, "out");
            Directory.CreateDirectory(outDir);
            await File.WriteAllBytesAsync(cabPath, cab, ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add(cabPath);
            psi.ArgumentList.Add("-F:*");
            psi.ArgumentList.Add(outDir);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("expand.exe did not start.");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);

            string? file = Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories).FirstOrDefault();
            if (process.ExitCode != 0 || file == null)
                throw new InvalidDataException($"Could not open the catalog cab (expand.exe exit code {process.ExitCode}).");
            return await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // a virus scanner may still hold the file; the temp folder gets cleaned eventually
            }
            catch (UnauthorizedAccessException)
            {
                // same as above
            }
        }
    }

    /// <summary>Extracts every file of <paramref name="cabPath"/> into <paramref name="folder"/>, keeping sub-folders.</summary>
    public static async Task ExtractAllAsync(string cabPath, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "expand.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(cabPath);
        psi.ArgumentList.Add("-F:*");
        psi.ArgumentList.Add(folder);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("expand.exe did not start.");
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new InvalidDataException($"Could not extract the package (expand.exe exit code {process.ExitCode}).");
    }

    /// <summary>Downloads a .cab, extracts it and parses the XML inside (UTF-8 or UTF-16).</summary>
    public static async Task<XDocument> DownloadXmlAsync(string url, CancellationToken ct)
    {
        byte[] cab = await Http.GetBytesAsync(url, ct).ConfigureAwait(false);
        byte[] xml = await ExtractFirstAsync(cab, ct).ConfigureAwait(false);
        return ParseXml(xml);
    }

    public static XDocument ParseXml(byte[] xml)
    {
        using var stream = new MemoryStream(xml);
        return XDocument.Load(stream);
    }
}
