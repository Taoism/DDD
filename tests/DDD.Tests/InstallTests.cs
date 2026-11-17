using System.Security.Cryptography;
using DDD.Core;
using DDD.Core.Install;
using DDD.Core.Oem;

namespace DDD.Tests;

public class InstallTests
{
    private static Finding F(string? url, string? sha256 = null, UpdateKind kind = UpdateKind.Driver) => new()
    {
        Kind = kind,
        Status = FindingStatus.UpdateAvailable,
        Source = "Update Catalog",
        Device = "Intel(R) Wireless Bluetooth(R)",
        AvailableVersion = "24.70.0.4",
        DownloadUrl = url,
        Sha256 = sha256,
    };

    [Theory]
    [InlineData("https://catalog.s.download.windowsupdate.com/d/x/abc_0123456789abcdef0123456789abcdef01234567.cab", true)]
    [InlineData("https://us.download.nvidia.com/Windows/621.24/621.24-desktop.exe", true)]
    [InlineData("https://www.nvidia.com/en-us/drivers/", false)]
    [InlineData("ftp://example.com/a.cab", false)]
    [InlineData(null, false)]
    public void Knows_what_it_can_download(string? url, bool expected) => Assert.Equal(expected, Downloader.CanDownload(F(url)));

    [Fact]
    public void Sha1_comes_from_microsofts_file_name()
    {
        Assert.Equal("f37bfe69b69ce29e48c34d59b8dd835b71f09229",
            Downloader.CatalogSha1("https://catalog.s.download.windowsupdate.com/d/msdownload/update/driver/drvs/2025/12/1537f801-8622-4c1c-86f5-f0324e5e2cb5_f37bfe69b69ce29e48c34d59b8dd835b71f09229.cab"));
        Assert.Null(Downloader.CatalogSha1("https://example.com/driver.cab"));
    }

    [Theory]
    [InlineData("Intel(R) Wireless Bluetooth(R) 24.70.0.4", "Intel_R_Wireless_Bluetooth_R_24.70.0.4")]
    [InlineData("a & b %PATH% \"c\"", "a_b_PATH_c")]
    [InlineData("???", "update")]
    public void Folder_names_are_safe_for_cmd(string input, string expected) => Assert.Equal(expected, Downloader.SafeName(input));

    [Fact]
    public void Long_file_names_keep_their_extension()
    {
        string name = Downloader.SafeFileName("1537f801-8622-4c1c-86f5-f0324e5e2cb5_f37bfe69b69ce29e48c34d59b8dd835b71f09229.cab");
        Assert.EndsWith(".cab", name);
        Assert.True(name.Length <= 84);
    }

    [Fact]
    public async Task Checksum_mismatch_is_rejected()
    {
        string file = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(file, "driver bytes");
            string good = Convert.ToHexString(SHA256.HashData("driver bytes"u8));
            Assert.Null(await Downloader.VerifyAsync(file, F("https://dell.com/a.cab", good), Downloader.PackageKind.Cab, CancellationToken.None));
            Assert.NotNull(await Downloader.VerifyAsync(file, F("https://dell.com/a.cab", new string('0', 64)), Downloader.PackageKind.Cab, CancellationToken.None));
            Assert.NotNull(await Downloader.VerifyAsync(file, F("https://x/y_0123456789abcdef0123456789abcdef01234567.cab"), Downloader.PackageKind.Cab, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Unsigned_installer_is_rejected()
    {
        string file = Path.Combine(Path.GetTempPath(), "ddd-unsigned-test.exe");
        await File.WriteAllBytesAsync(file, [0x4D, 0x5A, 0, 0]);
        try
        {
            Assert.NotNull(await Downloader.VerifyAsync(file, F("https://x/setup.exe"), Downloader.PackageKind.Exe, CancellationToken.None));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Script_runs_pnputil_per_folder_and_logs_exit_codes()
    {
        string script = DriverInstaller.BuildScript([(@"C:\t\a\driver", @"C:\t\driver1.log"), (@"C:\t\b\driver", @"C:\t\driver2.log")], restorePoint: true, @"C:\t");
        Assert.Contains("Checkpoint-Computer", script);
        Assert.Contains(@"/add-driver ""C:\t\a\driver\*.inf"" /subdirs /install > ""C:\t\driver1.log"" 2>&1", script);
        Assert.Contains(@"echo DDD-EXIT %errorlevel% >> ""C:\t\driver2.log""", script);
    }

    [Theory]
    [InlineData("Driver package added successfully.\r\nDDD-EXIT 0", 0, true, false)]
    [InlineData("System reboot is needed to complete install operations!\r\nDDD-EXIT 3010", 3010, true, true)]
    [InlineData("Driver package is up-to-date on device.\r\nDDD-EXIT 259", 259, false, false)]
    [InlineData("", -1, false, false)]
    public void Reads_pnputil_results(string log, int code, bool installed, bool restart)
    {
        var r = DriverInstaller.ParseLog(log);
        Assert.Equal(code, r.ExitCode);
        Assert.Equal(installed, r.Installed);
        Assert.Equal(restart, r.RestartNeeded);
        Assert.Equal(code == 259, r.AddedOnly);
    }

    [Fact]
    public void Bios_downloads_are_never_installable()
    {
        var item = new DownloadItem(F("https://dell.com/bios.exe", kind: UpdateKind.Bios));
        item.Set(DownloadState.Ready);
        Assert.False(item.CanInstall);
    }

    [Theory]
    [InlineData("02D09FCFD42305F2FE17CB2489C106B65C01611BAEF0A01A395952AF05890383", true)]
    [InlineData("379060cc654390a375d3963296e52db8ac12abaf9f45592ccffaed5b909db550", true)]
    [InlineData("5b7b116e48c820201edda11218959f74", false)]
    [InlineData(null, false)]
    public void Sha256_values_are_recognised(string? value, bool expected) =>
        Assert.Equal(expected, OemDriverSource.Sha256OrNull(value) != null);
}
