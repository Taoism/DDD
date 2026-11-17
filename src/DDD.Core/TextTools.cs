using System.Net;
using System.Text.RegularExpressions;

namespace DDD.Core;

/// <summary>HTML to readable text, and picking the "what changed" part out of a readme.</summary>
public static partial class TextTools
{
    /// <summary>Turns an HTML fragment or page into plain text with one line per block and "• " for list items.</summary>
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrEmpty(html))
            return "";
        string s = ScriptOrStyleRegex().Replace(html, " ");
        s = CommentRegex().Replace(s, " ");
        s = ListItemRegex().Replace(s, "\n• ");
        s = ParagraphBreakRegex().Replace(s, "\n\n");
        s = BlockBreakRegex().Replace(s, "\n");
        s = TagRegex().Replace(s, "");
        s = WebUtility.HtmlDecode(s);
        return NormalizeLines(s);
    }

    /// <summary>Trims every line, collapses runs of spaces and of blank lines, drops bullets with nothing after them.</summary>
    public static string NormalizeLines(string text)
    {
        var lines = new List<string>();
        foreach (string raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            string line = SpacesRegex().Replace(raw.Replace(' ', ' '), " ").Trim();
            if (line is "•" or "")
            {
                if (lines.Count > 0 && lines[^1].Length > 0)
                    lines.Add("");
                continue;
            }
            // lists stay together under their heading: no blank line in front of a bullet
            if (line.StartsWith('•') && lines.Count > 0 && lines[^1].Length == 0)
                lines.RemoveAt(lines.Count - 1);
            lines.Add(line);
        }
        return string.Join('\n', lines).Trim();
    }

    /// <summary>Collapses whitespace inside a single value (a table cell, a title).</summary>
    public static string Clean(string? html) =>
        string.IsNullOrEmpty(html) ? "" : SpacesRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(html, " ")), " ").Trim();

    /// <summary>
    /// From a readme, the part that says what changed: starts at the first heading such as
    /// "Summary of changes", "Fixes", "Enhancements" or at <paramref name="version"/>, and is cut to
    /// <paramref name="maxLength"/> characters at a line end.
    /// </summary>
    public static string ExtractChanges(string text, string? version = null, int maxLength = 2500)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        int start = -1;
        Match heading = ChangesHeadingRegex().Match(text);
        if (heading.Success)
            start = heading.Index;
        else if (!string.IsNullOrEmpty(version))
            start = text.IndexOf(version, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            start = 0;
        else
            start = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        return Cut(text[start..], maxLength);
    }

    /// <summary>Shortens <paramref name="text"/> to at most <paramref name="maxLength"/> characters, ending at a line break when possible.</summary>
    public static string Cut(string text, int maxLength)
    {
        text = text.Trim();
        if (text.Length <= maxLength)
            return text;
        int cut = text.LastIndexOf('\n', maxLength);
        if (cut < maxLength / 2)
            cut = maxLength;
        return text[..cut].TrimEnd() + "\n…";
    }

    [GeneratedRegex(@"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>|<head\b[^>]*>.*?</head\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ScriptOrStyleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ListItemRegex();

    [GeneratedRegex(@"<(/p|/h[1-6]|h[1-6]|/ul|/ol|/table|hr)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ParagraphBreakRegex();

    [GeneratedRegex(@"<(br|p|/div|div|/tr|/li|/dt|/dd)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex BlockBreakRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\f\v]+", RegexOptions.CultureInvariant, 2000)]
    private static partial Regex SpacesRegex();

    [GeneratedRegex(@"^[^\n]{0,40}\b(summary of changes|changes in this release|change history|what'?s new|enhancements?|fixes|fixed issues|fixed (gaming|general) bugs|new features|problems? fixed|improvements?)\b[^\n]{0,60}$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ChangesHeadingRegex();
}
