using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace DDD.Core.Install;

/// <summary>
/// Downloads update packages into a temporary folder, checks them (hash and/or signature) and
/// extracts driver .cab files so they are ready for <see cref="DriverInstaller"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class Downloader
{
    public static string DefaultFolder { get; } = Path.Combine(Path.GetTempPath(), "DDD", "downloads");

    /// <summary>True when the finding has a package DDD can download (.cab or .exe).</summary>
    public static bool CanDownload(Finding f) =>
        f.Status == FindingStatus.UpdateAvailable && Kind(f.DownloadUrl) != PackageKind.Other;

    internal enum PackageKind
    {
        Cab,
        Exe,
        Other,
    }

    internal static PackageKind Kind(string? url)
    {
        if (url == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return PackageKind.Other;
        string ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        return ext switch
        {
            ".cab" => PackageKind.Cab,
            ".exe" => PackageKind.Exe,
            _ => PackageKind.Other,
        };
    }

    /// <summary>Downloads, verifies and (for .cab) extracts <paramref name="item"/>; never throws for download problems, it sets Failed.</summary>
    public static async Task RunAsync(DownloadItem item, string root, CancellationToken ct)
    {
        var f = item.Finding;
        var kind = Kind(f.DownloadUrl);
        if (kind == PackageKind.Other)
        {
            item.Set(DownloadState.Failed, "No downloadable package for this update; use the details page.");
            return;
        }

        try
        {
            string folder = Path.Combine(root, FolderName(f));
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
            Directory.CreateDirectory(folder);
            item.Folder = folder;

            var uri = new Uri(f.DownloadUrl!);
            string file = Path.Combine(folder, SafeFileName(Path.GetFileName(uri.AbsolutePath)));
            await DownloadFileAsync(uri, file, item, ct).ConfigureAwait(false);
            item.FilePath = file;

            string? problem = await VerifyAsync(file, f, kind, ct).ConfigureAwait(false);
            if (problem != null)
            {
                File.Delete(file);
                item.Set(DownloadState.Failed, problem);
                return;
            }

            if (kind == PackageKind.Exe)
            {
                item.Signer = Authenticode.Signer(file);
                item.Set(f.Kind == UpdateKind.Bios ? DownloadState.Manual : DownloadState.Ready,
                    f.Kind == UpdateKind.Bios
                        ? "BIOS updates are not installed by DDD. Read the vendor's notes and run the update from the download folder."
                        : $"Installer signed by {item.Signer}.");
                return;
            }

            item.Set(DownloadState.Extracting);
            string extracted = Path.Combine(folder, "driver");
            await Cab.ExtractAllAsync(file, extracted, ct).ConfigureAwait(false);
            if (!Directory.EnumerateFiles(extracted, "*.inf", SearchOption.AllDirectories).Any())
            {
                item.Set(DownloadState.Manual, "The package has no driver (.inf) files; open the folder to see what it contains.");
                return;
            }
            item.DriverFolder = extracted;
            item.Set(DownloadState.Ready, "Driver package checked and extracted.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            item.Set(DownloadState.None, "Cancelled.");
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException or TaskCanceledException)
        {
            item.Set(DownloadState.Failed, ex.Message);
        }
    }

    private static async Task DownloadFileAsync(Uri uri, string file, DownloadItem item, CancellationToken ct)
    {
        item.Set(DownloadState.Downloading, "", 0);
        using var response = await Http.Shared.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        string part = file + ".part";
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var target = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
        {
            var buffer = new byte[1 << 16];
            long done = 0;
            int lastPercent = -2;
            int read;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                int percent = total is > 0 ? (int)(done * 100 / total.Value) : -1;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    item.Set(DownloadState.Downloading, "", percent);
                }
            }
        }
        File.Move(part, file, overwrite: true);
    }

    /// <summary>Null when the file checks out, otherwise why it was rejected.</summary>
    internal static async Task<string?> VerifyAsync(string file, Finding f, PackageKind kind, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(f.Sha256))
        {
            string actual = await HashAsync(file, SHA256.Create(), ct).ConfigureAwait(false);
            if (!actual.Equals(f.Sha256, StringComparison.OrdinalIgnoreCase))
                return "The download does not match the SHA-256 checksum the vendor published; it was deleted.";
        }
        else if (CatalogSha1(f.DownloadUrl) is { } sha1)
        {
            // SHA-1 is what Microsoft puts in the file name; it guards against a corrupt or swapped download.
            // Authenticity comes from the driver signature, which pnputil checks before installing.
            string actual = await HashAsync(file, SHA1.Create(), ct).ConfigureAwait(false); // NOSONAR S4790
            if (!actual.Equals(sha1, StringComparison.OrdinalIgnoreCase))
                return "The download does not match the checksum in Microsoft's file name; it was deleted.";
        }
        if (kind == PackageKind.Exe && !Authenticode.IsTrusted(file))
            return "The installer has no valid digital signature; it was deleted.";
        return null;
    }

    private static async Task<string> HashAsync(string file, HashAlgorithm algorithm, CancellationToken ct)
    {
        using (algorithm)
        {
            await using var stream = File.OpenRead(file);
            return Convert.ToHexString(await algorithm.ComputeHashAsync(stream, ct).ConfigureAwait(false));
        }
    }

    /// <summary>Microsoft's download servers name files "&lt;guid&gt;_&lt;sha1&gt;.cab"; the SHA-1, or null.</summary>
    internal static string? CatalogSha1(string? url)
    {
        if (url == null)
            return null;
        var m = CatalogSha1Regex().Match(url);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>A folder name for the update: device and version, safe for paths and for cmd scripts.</summary>
    internal static string FolderName(Finding f) => SafeName($"{f.Device} {f.AvailableVersion}");

    /// <summary>A safe file name that keeps its extension (long names are shortened before it).</summary>
    internal static string SafeFileName(string name)
    {
        string ext = Path.GetExtension(name);
        if (ext.Length is 0 or > 6 || !ext[1..].All(char.IsAsciiLetterOrDigit))
            return SafeName(name);
        return SafeName(Path.GetFileNameWithoutExtension(name)) + ext.ToLowerInvariant();
    }

    internal static string SafeName(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        string s = MultiUnderscoreRegex().Replace(sb.ToString(), "_").Trim('_', '.');
        if (s.Length > 80)
            s = s[..80];
        return s.Length == 0 ? "update" : s;
    }

    [GeneratedRegex(@"_([0-9a-f]{40})\.cab$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex CatalogSha1Regex();

    [GeneratedRegex("_{2,}", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MultiUnderscoreRegex();
}
