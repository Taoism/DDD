using System.Text;
using System.Xml.Linq;
using DDD.Core;
using DDD.Core.Bios;

namespace DDD.Tests;

public class BiosTests
{
    [Fact]
    public void Lenovo_package_descriptor()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Package name="BIOS_N32UJ" id="n32uj36w" version="1.78" hide="False">
              <Title default="EN"><Desc id="EN">BIOS Update Utility - 10/11</Desc></Title>
              <Severity type="2" />
              <ReleaseDate>2026-08-20</ReleaseDate>
              <DetectInstall><_Bios><Level>N32ETA2*</Level></_Bios></DetectInstall>
              <Dependencies><And><_Bios><Level>N32ET39*</Level><Level>N32ET9*</Level></_Bios></And></Dependencies>
              <Files>
                <Installer><File><Name>n32uj36w.exe</Name><Size>13398384</Size></File></Installer>
                <Readme default="EN"><File id="EN"><Name>n32uj36w.html</Name></File></Readme>
              </Files>
            </Package>
            """;
        var p = LenovoBios.ParsePackage(xml, "https://download.lenovo.com/pccbbs/mobiles/n32uj36w_2_.xml")!;
        Assert.Equal("1.78", p.Version);
        Assert.Equal(new DateTime(2026, 8, 20), p.Released);
        Assert.Equal("https://download.lenovo.com/pccbbs/mobiles/n32uj36w.exe", p.InstallerUrl);
        Assert.Equal("https://download.lenovo.com/pccbbs/mobiles/n32uj36w.html", p.ReadmeUrl);
        Assert.Equal(13398384, p.Size);
        Assert.Equal("Recommended", p.Severity);
        Assert.True(LenovoBios.FitsInstalled(p, "N32ET96W (1.72 )"));
        Assert.False(LenovoBios.FitsInstalled(p, "R1BET77W (1.40 )"));
        Assert.Equal("1.72", LenovoBios.InstalledVersion("N32ET96W (1.72 )"));
    }

    [Fact]
    public void Lenovo_catalog_bios_locations()
    {
        var doc = XDocument.Parse("""
            <packages>
              <package><location>https://download.lenovo.com/pccbbs/mobiles/n32uj36w_2_.xml</location><category>BIOS UEFI</category></package>
              <package><location>https://download.lenovo.com/pccbbs/mobiles/audio.xml</location><category>Audio</category></package>
            </packages>
            """);
        Assert.Equal(["https://download.lenovo.com/pccbbs/mobiles/n32uj36w_2_.xml"], LenovoBios.BiosLocations(doc));
    }

    [Fact]
    public void Hp_reference_and_installed_version()
    {
        var doc = XDocument.Parse("""
            <ImagePal><Solutions>
              <UpdateInfo><Id>sp173148</Id><Name>HP Z4-G4 Core-X WKS System BIOS</Name><Category>BIOS</Category><Version>03.00</Version>
                <Url>ftp.hp.com/pub/softpaq/sp173001-173500/sp173148.exe</Url><ReleaseNotesUrl>ftp.hp.com/pub/softpaq/sp173001-173500/sp173148.html</ReleaseNotesUrl>
                <Size>39928520</Size><DateReleased>2026-05-25</DateReleased><ReleaseType>Critical</ReleaseType>
                <DetailFileInformation><BiosDetail><BiosFamily>P62</BiosFamily></BiosDetail></DetailFileInformation></UpdateInfo>
              <UpdateInfo><Id>sp1</Id><Name>Audio</Name><Category>Driver - Audio</Category><Version>1.0</Version></UpdateInfo>
            </Solutions></ImagePal>
            """);
        var u = Assert.Single(HpBios.BiosUpdates(doc));
        Assert.Equal("https://ftp.hp.com/pub/softpaq/sp173001-173500/sp173148.exe", u.Url);
        Assert.Equal("P62", u.Family);
        Assert.Equal(new DateTime(2026, 5, 25), u.Released);
        Assert.Equal(("P62", "02.89"), HpBios.InstalledVersion("P62 Ver. 02.89"));
        Assert.Equal(["11.0.24h2", "11.0.23h2", "11.0.22h2", "11.0.21h2"], HpBios.OsCandidates(new SystemInfo { OsBuild = 26100 }));
    }

    [Fact]
    public void Dell_index_and_model_catalog()
    {
        const string ns = "openmanage/cm/dm";
        var index = XDocument.Parse($"""
            <ManifestIndex xmlns="{ns}">
              <GroupManifest><SupportedSystems><Brand><Model systemID="07E8" /></Brand></SupportedSystems>
                <ManifestInformation path="FOLDER14968537M/1/Inspiron_Desktop_07E8.cab" /></GroupManifest>
              <GroupManifest><SupportedSystems><Brand><Model systemID="0A5C" /></Brand></SupportedSystems>
                <ManifestInformation path="FOLDER1/1/Other_0A5C.cab" /></GroupManifest>
            </ManifestIndex>
            """);
        Assert.Equal("FOLDER1/1/Other_0A5C.cab", DellBios.ModelCatalogPath(index, "0a5c"));
        Assert.Null(DellBios.ModelCatalogPath(index, "FFFF"));

        // the model catalog is UTF-16 like Dell's real files
        string xml = $"""
            <?xml version="1.0" encoding="utf-16"?>
            <Manifest xmlns="{ns}">
              <SoftwareComponent releaseDate="November 20, 2020" vendorVersion="2.1.1" path="FOLDER04286776M/1/Vostro_5460_2.1.1.exe" size="6404656">
                <Name><Display lang="en"><![CDATA[Dell Vostro 5460 and Inspiron 5488 System BIOS]]></Display></Name>
                <ComponentType value="BIOS"><Display lang="en">BIOS</Display></ComponentType>
                <Description><Display lang="en"><![CDATA[This package contains the Dell system BIOS update.]]></Display></Description>
                <SupportedSystems><Brand><Model systemID="07E8" /></Brand></SupportedSystems>
                <ImportantInfo URL="https://www.dell.com/support/home/drivers/DriversDetails?driverId=DDGF1" />
                <Criticality value="1"><Display lang="en"><![CDATA[Recommended]]></Display></Criticality>
              </SoftwareComponent>
              <SoftwareComponent vendorVersion="1.9" path="x.exe"><ComponentType value="APAC" /><SupportedSystems><Brand><Model systemID="07E8" /></Brand></SupportedSystems></SoftwareComponent>
            </Manifest>
            """;
        var doc = Cab.ParseXml(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(xml)).ToArray());
        var c = Assert.Single(DellBios.BiosComponents(doc, "07E8"));
        Assert.Equal("2.1.1", c.VendorVersion);
        Assert.Equal(new DateTime(2020, 11, 20), c.Released);
        Assert.Equal("Recommended", c.Criticality);
        Assert.StartsWith("https://www.dell.com/", c.InfoUrl);
    }

    [Theory]
    [InlineData("1.21.0", "1.22.0", true)]
    [InlineData("1.22.0", "1.22.0", false)]
    [InlineData("02.89", "03.00", true)]
    [InlineData("A12", "A12", false)]
    public void Newer_bios(string installed, string available, bool expected) =>
        Assert.Equal(expected, BiosSource.IsNewer(installed, available, null, null));

    [Fact]
    public void Msi_board_gets_support_page()
    {
        var system = new SystemInfo
        {
            Manufacturer = "Micro-Star International Co., Ltd.",
            Model = "MS-7D75",
            BoardManufacturer = "Micro-Star International Co., Ltd.",
            BoardProduct = "MAG B650 TOMAHAWK WIFI (MS-7D75)",
            BiosVersion = "1.K0",
            BiosDate = new DateTime(2026, 8, 21),
        };
        var f = BoardVendorPages.Create(system);
        Assert.Equal(FindingStatus.CheckManually, f.Status);
        Assert.Equal("https://www.msi.com/Motherboard/MAG-B650-TOMAHAWK-WIFI/support#bios", f.InfoUrl);
        Assert.Equal("MSI", f.Source);
        Assert.Contains("1.K0", f.Notes);
    }
}
