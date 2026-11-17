using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.RegularExpressions;

namespace DDD.Core;

/// <summary>
/// A dotted numeric version ("32.0.101.7092", "1.78", "02.89"). Missing parts count as zero,
/// so 1.2 equals 1.2.0.0.
/// </summary>
public sealed partial class DriverVersion : IComparable<DriverVersion>, IEquatable<DriverVersion>
{
    private readonly long[] _parts;

    private DriverVersion(long[] parts) => _parts = parts;

    public IReadOnlyList<long> Parts => _parts;

    public static bool TryParse(string? text, [NotNullWhen(true)] out DriverVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] pieces = text.Trim().Split('.', StringSplitOptions.TrimEntries);
        if (pieces.Length > 6)
            return false;
        var parts = new long[pieces.Length];
        for (int i = 0; i < pieces.Length; i++)
        {
            if (pieces[i].Length is 0 or > 12 || !long.TryParse(pieces[i], NumberStyles.None, CultureInfo.InvariantCulture, out parts[i]))
                return false;
        }
        version = new DriverVersion(parts);
        return true;
    }

    public static DriverVersion? ParseOrNull(string? text) => TryParse(text, out var v) ? v : null;

    /// <summary>
    /// The last dotted version with three or four parts inside free text, e.g. a title like
    /// "Intel Corporation - Display - 32.0.101.7092". Falls back to a two-part version.
    /// </summary>
    public static DriverVersion? FindIn(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return null;
        DriverVersion? best = null;
        foreach (Match m in LongVersionRegex().Matches(text))
            best = ParseOrNull(m.Value) ?? best;
        if (best != null)
            return best;
        foreach (Match m in ShortVersionRegex().Matches(text))
            best = ParseOrNull(m.Value) ?? best;
        return best;
    }

    public int CompareTo(DriverVersion? other)
    {
        if (other is null)
            return 1;
        int n = Math.Max(_parts.Length, other._parts.Length);
        for (int i = 0; i < n; i++)
        {
            long a = i < _parts.Length ? _parts[i] : 0;
            long b = i < other._parts.Length ? other._parts[i] : 0;
            if (a != b)
                return a < b ? -1 : 1;
        }
        return 0;
    }

    public bool Equals(DriverVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is DriverVersion v && Equals(v);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        int last = _parts.Length - 1;
        while (last >= 0 && _parts[last] == 0)
            last--;
        for (int i = 0; i <= last; i++)
            hash.Add(_parts[i]);
        return hash.ToHashCode();
    }

    public override string ToString() => string.Join('.', _parts);

    public static bool operator ==(DriverVersion? a, DriverVersion? b) => a is null ? b is null : a.Equals(b);

    public static bool operator !=(DriverVersion? a, DriverVersion? b) => !(a == b);

    public static bool operator <(DriverVersion? a, DriverVersion? b) => a is null ? b is not null : a.CompareTo(b) < 0;

    public static bool operator >(DriverVersion? a, DriverVersion? b) => a is not null && a.CompareTo(b) > 0;

    public static bool operator <=(DriverVersion? a, DriverVersion? b) => !(a > b);

    public static bool operator >=(DriverVersion? a, DriverVersion? b) => !(a < b);

    [GeneratedRegex(@"(?<![\d.])\d{1,9}(?:\.\d{1,9}){2,3}(?![\d.])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LongVersionRegex();

    [GeneratedRegex(@"(?<![\d.])\d{1,9}\.\d{1,9}(?![\d.])", RegexOptions.CultureInvariant, 1000)]
    private static partial Regex ShortVersionRegex();
}
