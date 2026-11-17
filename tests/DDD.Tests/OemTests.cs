using System.Xml.Linq;
using DDD.Core;
using DDD.Core.Oem;

namespace DDD.Tests;

public class OemTests
{
    private static DeviceInfo Device(string version, params string[] ids) => new()
    {
        InstanceId = "dev",
        Name = "Intel Bluetooth",
        VersionText = version,
        HardwareIds = ids,
    };

    [Theory]
    [InlineData("PCI\\VEN_8086&DEV_4680", true)]
    [InlineData("PCI\\VEN_8086&DEV_4680&SUBSYS_7D751462", true)]
    [InlineData("PCI\\VEN_8086&DEV_468", false)]
    [InlineData("PCI\\VEN_8086&DEV_4681", false)]
    public void Catalog_id_covers_device(string catalogId, bool expected) =>
        Assert.Equal(expected, OemDriverSource.Covers(catalogId, Device("1.0", "PCI\\VEN_8086&DEV_4680&SUBSYS_7D751462&REV_0C")));

    [Fact]
    public void Dell_driver_components_by_pci_id()
    {
        var doc = XDocument.Parse("""
            <Manifest xmlns="openmanage/cm/dm">
              <SoftwareComponent releaseDate="August 23, 2016" vendorVersion="20.19.15.4483" path="FOLDER03879317M/6/Intel-Graphics.EXE" size="219693936">
                <Name><Display lang="en"><![CDATA[Intel HD Graphics Driver]]></Display></Name>
                <ComponentType value="DRVR" />
                <Description><Display lang="en"><![CDATA[This package contains the Intel graphics driver.]]></Display></Description>
                <SupportedDevices><Device><PCIInfo deviceID="0406" vendorID="8086" subDeviceID="0604" subVendorID="1028" /></Device></SupportedDevices>
                <SupportedSystems><Brand><Model systemID="07E8" /></Brand></SupportedSystems>
              </SoftwareComponent>
              <SoftwareComponent vendorVersion="1.0" path="bios.exe"><ComponentType value="BIOS" /><SupportedSystems><Brand><Model systemID="07E8" /></Brand></SupportedSystems></SoftwareComponent>
            </Manifest>
            """);
        var p = Assert.Single(OemDriverSource.DellPackages(doc, "07E8"));
        Assert.Equal(["PCI\\VEN_8086&DEV_0406&SUBSYS_06041028"], p.HardwareIds);
        Assert.Equal("https://downloads.dell.com/FOLDER03879317M/6/Intel-Graphics.EXE", p.DownloadUrl);
        Assert.Equal("20.19.15.4483", OemDriverSource.VersionFor(p, Device("20.19.15.4000", "PCI\\VEN_8086&DEV_0406&SUBSYS_06041028&REV_06")));
        Assert.Null(OemDriverSource.VersionFor(p, Device("1.0", "PCI\\VEN_8086&DEV_0406&SUBSYS_00001028")));
        Assert.Empty(OemDriverSource.DellPackages(doc, "0A5C"));
    }

    [Fact]
    public void Hp_cva_devices()
    {
        const string cva = "[General]\r\nVersion= 24.30.1.1\r\n\r\n[Devices]\r\nUSB\\VID_8087&PID_0036=\"Intel Wi-Fi 7 BE200\"\r\nUSB\\VID_8087&PID_0033=\"Intel Wi-Fi 6E AX211\"\r\n\r\n[Devices_INFPath]\r\nW11_INFPath=src\\Drivers\\FMP\r\n";
        Assert.Equal(["USB\\VID_8087&PID_0036", "USB\\VID_8087&PID_0033"], OemDriverSource.CvaDevices(cva));

        var update = XElement.Parse("""
            <UpdateInfo><Id>sp173987</Id><Name>Intel Wireless Bluetooth Driver</Name><Category>Driver - Network</Category><Version>24.30.1.1</Version>
              <Url>ftp.hp.com/pub/softpaq/sp173501-174000/sp173987.exe</Url><DateReleased>2026-07-29</DateReleased><ReleaseType>Routine</ReleaseType></UpdateInfo>
            """);
        var p = OemDriverSource.HpPackage(update, cva)!;
        Assert.Equal("https://ftp.hp.com/pub/softpaq/sp173501-174000/sp173987.exe", p.DownloadUrl);
        Assert.Equal("24.30.1.1", OemDriverSource.VersionFor(p, Device("24.20.0.1", "USB\\VID_8087&PID_0033&REV_0000")));
    }

    [Fact]
    public void Lenovo_package_driver_versions()
    {
        const string xml = """
            <Package name="QSXM_NZ7WN" id="nz7wn09w" version="v59">
              <Title default="EN"><Desc id="EN">Qualcomm Snapdragon X55 5G Wireless WAN Driver</Desc></Title>
              <Severity type="2" />
              <ReleaseDate>2024-01-26</ReleaseDate>
              <DetectInstall><And>
                <_Driver><HardwareID><![CDATA[PCI\VEN_105B&DEV_E0AB]]></HardwareID><Version>1.0.4.2^</Version></_Driver>
              </And></DetectInstall>
              <Dependencies><Or><_PnPID><![CDATA[PCI\VEN_105B&DEV_E0AB]]></_PnPID></Or></Dependencies>
              <Files><Installer><File><Name>nz7wn09w.exe</Name><Size>116941192</Size></File></Installer>
                <Readme default="EN"><File id="EN"><Name>nz7wn09w.txt</Name></File></Readme></Files>
            </Package>
            """;
        var p = OemDriverSource.LenovoPackage(xml, "https://download.lenovo.com/pccbbs/mobiles/nz7wn09w_2_.xml", "20XWCTO1WW")!;
        Assert.Equal("1.0.4.2", OemDriverSource.VersionFor(p, Device("1.0.3.0", "PCI\\VEN_105B&DEV_E0AB&SUBSYS_4D7517AA&REV_00")));
        Assert.Equal("https://download.lenovo.com/pccbbs/mobiles/nz7wn09w.exe", p.DownloadUrl);
        Assert.Equal("https://download.lenovo.com/pccbbs/mobiles/nz7wn09w.txt", p.ReleaseNotesUrl);
        Assert.Equal("Recommended", p.Severity);
    }
}
