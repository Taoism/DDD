using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DDD.Controls;
using DDD.Core;

namespace DDD.Dialogs;

/// <summary>About DDD: the poster on the left, what the program does on the right.</summary>
public sealed class AboutDialog : DialogWindow
{
    public AboutDialog() : base("About DDD", 760)
    {
        var poster = new Border
        {
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            Width = 330,
            Height = 440,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new Image
            {
                Source = new Bitmap(AssetLoader.Open(new Uri("avares://DDD/Assets/about.jpg"))),
                Stretch = Stretch.UniformToFill,
            },
        };

        var title = NeonSign.Logo(36);
        var version = Label("Version " + Http.AppVersion, muted: true);

        var text = Column(10,
            title,
            version,
            Rule(),
            Label("Finds newer drivers and BIOS for this PC, tells you what they fix, and downloads, checks and installs the drivers you choose. BIOS updates are only downloaded."),
            Label("Sources: Windows Update, the Microsoft Update Catalog, NVIDIA, and the Dell, HP and Lenovo catalogs. " +
                  "For other board makers it links the BIOS support page."),
            Label("With \"Prefer maker's drivers\" on, the PC maker's or chip maker's driver is recommended over Windows Update's."),
            Label("Always read the vendor's notes before flashing a BIOS.", muted: true));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 22 };
        grid.Children.Add(poster);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        SetBody(grid, "Close", null);
    }
}
