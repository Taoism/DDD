using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace DDD.Controls;

/// <summary>The "DDD" title: heavy block letters, each D one shade lighter, like the app icon.</summary>
public static class NeonSign
{
    private static readonly FontFamily Block = new("Arial Black, Segoe UI Black, Impact, sans-serif");

    // the three D's of the app icon, dark to light
    private static readonly string[] IconShades = ["#FF2E60AA", "#FF3D7FD6", "#FF82BCFF"];

    public static Control Logo(double size)
    {
        var text = new TextBlock
        {
            FontFamily = Block,
            FontSize = size,
            VerticalAlignment = VerticalAlignment.Center,
        };
        foreach (string shade in IconShades)
            text.Inlines!.Add(new Run("D") { Foreground = new SolidColorBrush(Color.Parse(shade)) });
        return text;
    }
}
