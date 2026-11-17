using System.Text.RegularExpressions;

namespace DDD.Core.Bios;

/// <summary>
/// Board makers without a public catalog (MSI, ASUS, Gigabyte, ASRock ...; MSI's API also refuses
/// automated clients). DDD shows the installed BIOS and links the board's BIOS download page.
/// </summary>
internal static partial class BoardVendorPages
{
    public static Finding Create(SystemInfo system)
    {
        string oem = Vendors.ShortName(system.Manufacturer);
        string boardMaker = Vendors.ShortName(system.BoardManufacturer);
        string product = ProductName(system.BoardProduct);
        string url = SupportUrl(oem, boardMaker, product, system);
        string date = system.BiosDate is { } d ? $" from {d:yyyy-MM-dd}" : "";
        string maker = oem is "Dell" or "HP" or "Lenovo" ? oem : boardMaker;

        string notes =
            $"Installed: BIOS {system.BiosVersion}{date} on {maker} {product}.\n\n" +
            $"{maker} does not publish a BIOS catalog that programs can read, so DDD can't tell whether a newer version exists. " +
            "Open the support page and compare the newest version there with the installed one; the BIOS list on that page " +
            "says what each version fixes or adds (CPU support, memory compatibility, security fixes such as microcode updates).\n\n" +
            "Before flashing: read the vendor's notes, keep the PC on stable power, and don't skip versions the vendor marks as required steps.";
        if (oem is "Dell" or "HP" or "Lenovo")
            notes = $"This model was not found in {oem}'s update catalog.\n\n" + notes;

        return new Finding
        {
            Kind = UpdateKind.Bios,
            Status = FindingStatus.CheckManually,
            Source = maker.Length > 0 ? maker : "BIOS",
            Device = $"BIOS – {system.DisplayName}",
            Category = "BIOS",
            InstalledVersion = system.BiosVersion,
            InstalledDate = system.BiosDate,
            Title = $"{maker} {product} BIOS",
            Provider = maker,
            Notes = notes,
            InfoUrl = url,
        };
    }

    /// <summary>"MAG B650 TOMAHAWK WIFI (MS-7D75)" → "MAG B650 TOMAHAWK WIFI".</summary>
    internal static string ProductName(string boardProduct) => MsModelRegex().Replace(boardProduct, "").Trim();

    internal static string SupportUrl(string oem, string boardMaker, string product, SystemInfo system)
    {
        string dashed = Uri.EscapeDataString(SpacesRegex().Replace(product, "-"));
        string query = Uri.EscapeDataString(product);
        return oem switch
        {
            "Dell" => "https://www.dell.com/support/home/drivers",
            "HP" => "https://support.hp.com/drivers",
            "Lenovo" => $"https://pcsupport.lenovo.com/search?query={Uri.EscapeDataString(system.Model)}",
            _ => boardMaker switch
            {
                "MSI" => $"https://www.msi.com/Motherboard/{dashed}/support#bios",
                "ASUS" => $"https://www.asus.com/supportonly/{query}/helpdesk_bios/",
                "Gigabyte" => $"https://www.gigabyte.com/Motherboard/{dashed}/support#support-dl-bios",
                "ASRock" => $"https://www.asrock.com/support/index.asp?cat=BIOS&Model={query}",
                _ => "https://www.bing.com/search?q=" + Uri.EscapeDataString($"{boardMaker} {product} BIOS download"),
            },
        };
    }

    [GeneratedRegex(@"\s*\(MS-\w+\)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 1000)]
    private static partial Regex MsModelRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex SpacesRegex();
}
