using DDD.Core;

namespace DDD.Tests;

public class DriverVersionTests
{
    [Theory]
    [InlineData("32.0.101.7092", "32.0.101.7088", 1)]
    [InlineData("1.2", "1.2.0.0", 0)]
    [InlineData("10.1.45.9", "10.1.45.10", -1)]
    [InlineData("02.89", "03.00", -1)]
    [InlineData("621.24", "621.24", 0)]
    public void Compares_numerically(string a, string b, int expected)
    {
        var va = DriverVersion.ParseOrNull(a)!;
        var vb = DriverVersion.ParseOrNull(b)!;
        Assert.Equal(expected, Math.Sign(va.CompareTo(vb)));
        Assert.Equal(expected == 0, va == vb);
        if (expected == 0)
            Assert.Equal(va.GetHashCode(), vb.GetHashCode());
    }

    [Theory]
    [InlineData("1.K0")]
    [InlineData("")]
    [InlineData("A12")]
    [InlineData("1..2")]
    [InlineData("-1.2")]
    public void Rejects_non_numeric(string text) => Assert.Null(DriverVersion.ParseOrNull(text));

    [Theory]
    [InlineData("Intel Corporation Display Driver Update (32.0.101.7088)", "32.0.101.7088")]
    [InlineData("Intel - SoftwareComponent - 1.71.99.0", "1.71.99.0")]
    [InlineData("Realtek - Net - 10.68.815.2023", "10.68.815.2023")]
    [InlineData("GeForce driver 621.24", "621.24")]
    [InlineData("no version here", null)]
    public void Finds_version_in_titles(string title, string? expected) =>
        Assert.Equal(expected, DriverVersion.FindIn(title)?.ToString());

    [Fact]
    public void Null_ordering_is_safe()
    {
        DriverVersion? none = null;
        var one = DriverVersion.ParseOrNull("1.0");
        Assert.True(one > none);
        Assert.False(none > one);
        Assert.True(none < one);
    }
}

public class VendorsTests
{
    [Theory]
    [InlineData("Micro-Star International Co., Ltd.", "MSI")]
    [InlineData("ASUSTeK COMPUTER INC.", "ASUS")]
    [InlineData("Intel Corporation", "Intel")]
    [InlineData("Advanced Micro Devices, Inc.", "AMD")]
    [InlineData("HP", "HP")]
    [InlineData("Hewlett-Packard", "HP")]
    [InlineData("LENOVO", "Lenovo")]
    [InlineData("Dell Inc.", "Dell")]
    [InlineData("Contoso Widgets Corporation", "Contoso Widgets")]
    [InlineData("HPE Something", "HPE Something")]
    public void Short_names(string input, string expected) => Assert.Equal(expected, Vendors.ShortName(input));

    [Theory]
    [InlineData("Realtek Semiconductor Corp.", "Realtek", true)]
    [InlineData("INTEL", "Intel Corporation", true)]
    [InlineData("Intel", "Realtek", false)]
    [InlineData("", "", false)]
    public void Same_company(string a, string b, bool expected) => Assert.Equal(expected, Vendors.SameCompany(a, b));
}

public class TextToolsTests
{
    [Fact]
    public void Html_becomes_lines_and_bullets()
    {
        string text = TextTools.HtmlToText("<script>x()</script><p>Game Ready for <b>X</b></p><ul><li>Fix A</li><li>Fix &amp; B</li></ul><br>");
        Assert.Equal("Game Ready for X\n• Fix A\n• Fix & B", text);
    }

    [Fact]
    public void Extracts_changes_section()
    {
        string readme = "Lenovo BIOS Update Utility\nInstallation steps\nblah\n\nSummary of Changes\n<1.78>\n- Fixed an issue where the fan ran at full speed.\n";
        string changes = TextTools.ExtractChanges(readme, "1.78");
        Assert.StartsWith("Summary of Changes", changes);
        Assert.Contains("fan ran at full speed", changes);
    }

    [Fact]
    public void Cut_ends_at_a_line()
    {
        string text = string.Join('\n', Enumerable.Range(1, 100).Select(i => "line number " + i));
        string cut = TextTools.Cut(text, 100);
        Assert.EndsWith("\n…", cut);
        Assert.True(cut.Length <= 102);
    }
}

public class MergeTests
{
    private static Finding F(string source, FindingStatus status, string available, string? id = "dev1", string notes = "") => new()
    {
        Kind = UpdateKind.Driver,
        Status = status,
        Source = source,
        Device = "Intel Wi-Fi",
        DeviceInstanceId = id,
        AvailableVersion = available,
        Notes = notes,
    };

    [Fact]
    public void Same_driver_from_two_sources_becomes_one_line()
    {
        var merged = Scanner.Merge([
            F("Windows Update", FindingStatus.UpdateAvailable, "24.80.0.2", notes: "short") with { DownloadUrl = "https://x/a.cab" },
            F("Update Catalog", FindingStatus.UpdateAvailable, "24.80.0.2.0", notes: "a much longer description of the update"),
        ]);
        var only = Assert.Single(merged);
        Assert.Equal("Update Catalog + Windows Update", only.Source);
        Assert.Equal("a much longer description of the update", only.Notes);
        Assert.Equal("https://x/a.cab", only.DownloadUrl);
    }

    [Fact]
    public void Up_to_date_is_dropped_when_another_source_has_an_update()
    {
        var merged = Scanner.Merge([
            F("Update Catalog", FindingStatus.UpToDate, "24.40.0.4"),
            F("Windows Update", FindingStatus.UpdateAvailable, "24.80.0.2"),
            F("Update Catalog", FindingStatus.UpToDate, "1.0", id: "dev2"),
        ]);
        Assert.Equal(2, merged.Count);
        Assert.Equal(FindingStatus.UpdateAvailable, merged[0].Status);
        Assert.Equal("dev2", merged[1].DeviceInstanceId);
    }

    [Fact]
    public void Maker_driver_is_preferred_over_newer_windows_update_driver()
    {
        var merged = Scanner.Merge([
            F("Windows Update", FindingStatus.UpdateAvailable, "24.80.0.2"),
            F("Dell", FindingStatus.UpdateAvailable, "24.60.0.1", notes: "Dell's package"),
        ]);
        var only = Assert.Single(merged);
        Assert.Equal("Dell", only.Source);
        Assert.Contains("Windows Update: 24.80.0.2", only.Notes);
        Assert.Contains("not from the device's maker", only.Notes);
    }

    [Fact]
    public void Without_preference_the_highest_version_wins()
    {
        var merged = Scanner.Merge([
            F("Windows Update", FindingStatus.UpdateAvailable, "24.80.0.2"),
            F("Dell", FindingStatus.UpdateAvailable, "24.60.0.1"),
        ], preferMaker: false);
        Assert.Equal("Windows Update", Assert.Single(merged).Source);
    }

    [Fact]
    public void Update_against_makers_current_driver_is_marked_optional()
    {
        var merged = Scanner.Merge([
            F("Lenovo", FindingStatus.UpToDate, "24.60.0.1"),
            F("Windows Update", FindingStatus.UpdateAvailable, "24.80.0.2"),
        ]);
        var only = Assert.Single(merged);
        Assert.Equal("Optional", only.Severity);
        Assert.Contains("Lenovo's own newest driver", only.Notes);
    }

    [Fact]
    public void Ranks_sources()
    {
        Assert.Equal(0, Scanner.Rank(F("HP", FindingStatus.UpdateAvailable, "1")));
        Assert.Equal(1, Scanner.Rank(F("NVIDIA", FindingStatus.UpdateAvailable, "1")));
        Assert.Equal(2, Scanner.Rank(F("Update Catalog + Windows Update", FindingStatus.UpdateAvailable, "1")));
        Assert.Equal(3, Scanner.Rank(F("Windows Update", FindingStatus.UpdateAvailable, "1")));
    }
}
