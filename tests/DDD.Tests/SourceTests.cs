using DDD.Core;
using DDD.Core.Sources;

namespace DDD.Tests;

public class CatalogClientTests
{
    private const string Id1 = "39b8bb72-c07e-4a02-9b90-1dece9178023";
    private const string Id2 = "c69f60f8-b2c5-4b2b-9daf-fdf2dcb4aa8b";

    // trimmed copy of a real search page row (2026-11)
    private static readonly string SearchPage = $$"""
        <table><tr id="headerRow"><td>Title</td></tr>
        <tr id="{{Id1}}_R1"> <td class="resultsbottomBorder resultspadding resultsIconWidth " id="{{Id1}}_C0_R1"> </td><td class="resultsbottomBorder resultspadding" id="{{Id1}}_C1_R1"> <a id='{{Id1}}_link' href= "javascript:void(0);" onclick='goToDetails("{{Id1}}");' class="contentTextItemSpacerNoBreakLink"> Intel Corporation Display Driver Update (32.0.101.7088) </a> </td><td class="resultsbottomBorder resultspadding" id="{{Id1}}_C2_R1"> Windows 11 Client, version 22H2 and later, Servicing Drivers, Windows 11 Client, version 22H2 and later, Upgrade &amp; Servicing Drivers </td><td class="resultsbottomBorder resultspadding" id="{{Id1}}_C3_R1"> Drivers (Video) </td><td class="resultsbottomBorder resultspadding " id="{{Id1}}_C4_R1"> 6/16/2026 </td><td class="resultsbottomBorder resultspadding" id="{{Id1}}_C5_R1"> 32.0.101.7088 </td><td class="resultsbottomBorder resultspadding resultsSizeWidth" id="{{Id1}}_C6_R1"> <span id="{{Id1}}_size">525.0 MB</span> <span class="noDisplay" id="{{Id1}}_originalSize">550478830</span> </td><td class="resultsbottomBorder resultsButtonWidth" id="{{Id1}}_C7_R1"> <input id="{{Id1}}" class="flatBlueButtonDownload focus-only" type="button" value='Download' /> </td> </tr>
        <tr id="{{Id2}}_R2"> <td id="{{Id2}}_C0_R2"> </td><td id="{{Id2}}_C1_R2"> <a id='{{Id2}}_link'> Intel Corporation Display Driver Update (32.0.101.7088) </a> </td><td id="{{Id2}}_C2_R2"> Windows 10, Vibranium and later, Servicing Drivers </td><td id="{{Id2}}_C3_R2"> Drivers (Video) </td><td id="{{Id2}}_C4_R2"> 6/16/2026 </td><td id="{{Id2}}_C5_R2"> 32.0.101.7088 </td><td id="{{Id2}}_C6_R2"> <span id="{{Id2}}_size">525.0 MB</span> </td></tr>
        </table>
        """;

    [Fact]
    public void Parses_search_rows()
    {
        var rows = CatalogClient.ParseSearch(SearchPage);
        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal(Id1, first.UpdateId);
        Assert.Equal("Intel Corporation Display Driver Update (32.0.101.7088)", first.Title);
        Assert.Contains("Upgrade & Servicing Drivers", first.Products);
        Assert.Equal("Drivers (Video)", first.Classification);
        Assert.Equal(new DateTime(2026, 6, 16), first.LastUpdated);
        Assert.Equal("32.0.101.7088", first.Version!.ToString());
        Assert.Equal(550478830, first.Size);
        Assert.Null(rows[1].Size);
    }

    [Fact]
    public void Parses_details_page()
    {
        const string html = """
            <span id="ScopedViewHandler_titleText">Intel Corporation Display Driver Update (32.0.101.7088)</span>
            <span id="ScopedViewHandler_desc">Intel Corporation Display driver update released in June 2026</span>
            <div id="archDiv"> <span id="ScopedViewHandler_labelArchitecture_Separator" class="labelTitle">Architecture:</span> AMD64 </div>
            <span id="ScopedViewHandler_manufacturer">Intel Corporation</span>
            <span id="ScopedViewHandler_driverClass">Video</span>
            <span id="ScopedViewHandler_driverModel">Intel(R) UHD Graphics P750,Intel(R) UHD Graphics 770</span>
            <span id="ScopedViewHandler_driverProvider">Intel Corporation</span>
            <span id="ScopedViewHandler_version">32.0.101.7088</span>
            <span id="ScopedViewHandler_versionDate">6/17/2026</span>
            <div id="moreInfoDiv" class="textDoubleSpacer"> <span class="labelTitle">More information:</span> <div> <a target="catalogNew" href="https://learn.microsoft.com/x"> https://learn.microsoft.com/x </a></div></div>
            <div id="suportUrlDiv"><a href="https://www.intel.com/support">x</a></div>
            """;
        var d = CatalogClient.ParseDetails("id", html)!;
        Assert.Equal("AMD64", d.Architecture);
        Assert.Equal("Intel Corporation", d.Provider);
        Assert.Equal("Video", d.DriverClass);
        Assert.Equal(new DateTime(2026, 6, 17), d.VersionDate);
        Assert.Equal("https://learn.microsoft.com/x", d.MoreInfoUrl);
        Assert.Equal("https://www.intel.com/support", d.SupportUrl);
        Assert.Null(CatalogClient.ParseDetails("id", "<html>nothing</html>"));
    }

    [Fact]
    public void Parses_download_dialog()
    {
        const string html = "downloadInformation[0].files[0].url = 'https://catalog.s.download.windowsupdate.com/d/x/abc.cab';";
        Assert.Equal(["https://catalog.s.download.windowsupdate.com/d/x/abc.cab"], CatalogClient.ParseDownloadUrls(html));
    }

    [Fact]
    public void Detects_throttle_page()
    {
        Assert.True(CatalogClient.IsThrottled("<span id=\"ctl00_catalogBody_searchThrottled\">Too many search requests.</span>"));
        Assert.False(CatalogClient.IsThrottled("<span id=\"ctl00_catalogBody_noResultText\"></span>"));
    }
}

public class CatalogSourceTests
{
    private static DeviceInfo Device(params string[] ids) => new()
    {
        InstanceId = "x",
        Name = "dev",
        HardwareIds = ids,
        VersionText = "1.0",
    };

    [Theory]
    [InlineData("PCI\\VEN_8086&DEV_4680&SUBSYS_7D751462&REV_0C", "PCI\\VEN_8086&DEV_4680")]
    [InlineData("USB\\VID_046D&PID_C52B&REV_1211&MI_00", "USB\\VID_046D&PID_C52B")]
    [InlineData("HDAUDIO\\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_14627D75&REV_1001", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0897")]
    [InlineData("ACPI\\VEN_INTC&DEV_1055", "ACPI\\VEN_INTC&DEV_1055")]
    public void Query_uses_vendor_and_device(string id, string expected) => Assert.Equal(expected, CatalogSource.Query(Device(id)));

    [Theory]
    [InlineData("Windows 11 Client, version 24H2 and later, Servicing Drivers", 26100, true)]
    [InlineData("Windows 10, Vibranium and later, Servicing Drivers", 26100, true)]
    [InlineData("Windows 10, version 1903 and later, Servicing Drivers", 19045, true)]
    [InlineData("Windows 10 S, Windows 10 Creators Update", 26100, false)]
    [InlineData("Windows 11 Client, version 24H2 and later", 19045, false)]
    public void Applies_to_this_windows(string products, int build, bool expected)
    {
        var entry = new CatalogEntry("id", "Intel - System - 1.0.0.0", products, "Drivers (Other Hardware)", null, "1.0.0.0", null);
        Assert.Equal(expected, CatalogSource.AppliesTo(entry, new SystemInfo { OsBuild = build }));
    }

    [Fact]
    public void Generic_microsoft_links_are_not_release_notes()
    {
        Assert.Null(CatalogSource.UsefulLink("https://learn.microsoft.com/en-us/windows-hardware/drivers/dashboard/hardware-submission-support"));
        Assert.Equal("https://www.intel.com/x", CatalogSource.UsefulLink("https://www.intel.com/x"));
    }

    [Fact]
    public void Arm64_rows_are_skipped()
    {
        var entry = new CatalogEntry("id", "Qualcomm - ARM64 - 1.0", "Windows 11 Client", "Drivers", null, "1.0", null);
        Assert.False(CatalogSource.AppliesTo(entry, new SystemInfo { OsBuild = 26100 }));
    }
}

public class NvidiaTests
{
    [Theory]
    [InlineData("32.0.16.2124", "621.24")]
    [InlineData("31.0.15.5222", "552.22")]
    [InlineData("27.21.14.6079", "460.79")]
    [InlineData("1.2.3", null)]
    public void Windows_version_to_nvidia_version(string windows, string? expected) =>
        Assert.Equal(expected, NvidiaSource.InstalledVersion(windows));

    [Fact]
    public void Parses_lookup_answer()
    {
        const string json = """
            {"DriverAttributes":{"ID":285320,"Name":"GeForce Game Ready Driver","ReleaseDateTime":"2026-11-11T16:00:00-08:00","Version":"621.24","IsWHQL":1,
             "DetailsURL":"https://www.nvidia.com/en-us/drivers/details/285320/",
             "DownloadURL":"https://international-gfe.download.nvidia.com/Windows/621.24/621.24-g.exe",
             "DownloadURLAdmin":"https://us.download.nvidia.com/Windows/621.24/621.24-desktop.exe"}}
            """;
        var d = NvidiaSource.ParseLookup(json)!;
        Assert.Equal("285320", d.Id);
        Assert.Equal("621.24", d.Version);
        Assert.Equal(new DateTime(2026, 11, 11), d.Released);
        Assert.Equal("https://us.download.nvidia.com/Windows/621.24/621.24-desktop.exe", d.DownloadUrl);
        Assert.True(d.IsWhql);
        Assert.Null(NvidiaSource.ParseLookup("""{"DriverAttributes":null}"""));
    }

    [Fact]
    public void Parses_release_notes()
    {
        string notes = Uri.EscapeDataString("<p><b>Game Ready for X</b></p><p>Fixed General Bugs</p><ul><li>Photos: wrong colors [6996250]</li></ul>");
        string json = $$$"""{"driverDetails":{"IDS":[{"downloadInfo":{"ReleaseNotes":"{{{notes}}}","OtherNotes":"N%2FA","DownloadURLFileSize":"1.02 GB"}}]}}""";
        var (text, size) = NvidiaSource.ParseDetails(json);
        Assert.Equal("Game Ready for X\n\nFixed General Bugs\n• Photos: wrong colors [6996250]", text);
        Assert.Equal((long)(1.02 * 1024 * 1024 * 1024), size);
    }

    [Fact]
    public void Lookup_url_carries_device_and_build()
    {
        string url = NvidiaSource.LookupUrl("1e84", new SystemInfo { OsBuild = 26100 }, studio: true);
        string decoded = Uri.UnescapeDataString(url);
        Assert.Contains("\"1E84_10DE\"", decoded);
        Assert.Contains("\"osB\":\"26100\"", decoded);
        Assert.Contains("\"isCRD\":\"1\"", decoded);
    }
}
