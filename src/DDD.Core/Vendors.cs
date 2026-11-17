namespace DDD.Core;

/// <summary>Vendor name normalisation.</summary>
public static class Vendors
{
    private static readonly (string Match, string Short)[] Known =
    [
        ("Micro-Star", "MSI"),
        ("MSI", "MSI"),
        ("ASUSTeK", "ASUS"),
        ("ASUS", "ASUS"),
        ("Gigabyte", "Gigabyte"),
        ("ASRock", "ASRock"),
        ("Hewlett-Packard", "HP"),
        ("HP", "HP"),
        ("Dell", "Dell"),
        ("Alienware", "Dell"),
        ("Lenovo", "Lenovo"),
        ("Intel", "Intel"),
        ("Advanced Micro Devices", "AMD"),
        ("AMD", "AMD"),
        ("NVIDIA", "NVIDIA"),
        ("Realtek", "Realtek"),
        ("Microsoft", "Microsoft"),
        ("Acer", "Acer"),
        ("Samsung", "Samsung"),
        ("Biostar", "Biostar"),
    ];

    /// <summary>"Micro-Star International Co., Ltd." → "MSI", "Intel Corporation" → "Intel"; unknown names are trimmed of corporate suffixes.</summary>
    public static string ShortName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        string n = name.Trim();
        foreach (var (match, shortName) in Known)
        {
            if (n.StartsWith(match, StringComparison.OrdinalIgnoreCase)
                && (n.Length == match.Length || !char.IsLetter(n[match.Length])))
            {
                return shortName;
            }
        }
        foreach (string suffix in (string[])[", Inc.", " Inc.", " Inc", " Corporation", " Corp.", " Corp", " Co., Ltd.", " Co.,Ltd.", " Ltd.", " Ltd", " LLC", " GmbH"])
        {
            if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                n = n[..^suffix.Length].TrimEnd(',', ' ');
        }
        return n;
    }

    /// <summary>
    /// Key for "is this the same company": lower-case first word of the short name
    /// ("Realtek Semiconductor Corp." and "Realtek" both give "realtek").
    /// </summary>
    public static string Key(string? name)
    {
        string s = ShortName(name);
        int space = s.IndexOfAny([' ', ',', '-', '(']);
        return (space > 0 ? s[..space] : s).ToLowerInvariant();
    }

    public static bool SameCompany(string? a, string? b)
    {
        string ka = Key(a);
        return ka.Length > 0 && ka == Key(b);
    }
}
