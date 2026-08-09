using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// Downloads-Tab im Kroste-Card-Look. Rows als Cards mit Preview-Cover (aus
/// ZIP extrahiert), Titel + INSTALLIERT-Badge + Meta, rechts Install (accent)
/// und Löschen (danger) pro Row.
/// </summary>
public sealed class DownloadsView : UserControl
{
    public DownloadsView()
    {
        Content = new DockPanel
        {
            Margin = new Thickness(20, 16, 20, 14),
            Children =
            {
                WithDock(BuildToolbar(), Dock.Top),
                WithDock(BuildPathLabel(), Dock.Top),
                WithDock(BuildSummary(), Dock.Bottom),
                BuildList(),
            },
        };
    }

    private static Control BuildToolbar()
    {
        var refreshBtn = new Button { Content = "↺  Aktualisieren" };
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(DownloadsViewModel.RefreshCommand)));
        var openBtn = new Button { Content = "📂  Downloads-Ordner" };
        openBtn.Bind(Button.CommandProperty, new Binding(nameof(DownloadsViewModel.OpenDownloadsFolderCommand)));

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
            Children = { refreshBtn, openBtn },
        };
    }

    private static Control BuildPathLabel()
    {
        var t = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        };
        t.Classes.Add("muted");
        t.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadsViewModel.DownloadsDir))
        { StringFormat = "Downloads: {0}" });
        return t;
    }

    private static Control BuildSummary()
    {
        var t = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
        t.Classes.Add("muted");
        t.Bind(TextBlock.TextProperty, new Binding(nameof(DownloadsViewModel.Summary)));
        return t;
    }

    private static Control BuildList()
    {
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(DownloadsViewModel.Rows)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(DownloadsViewModel.Selected))
        { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<ModRow>((row, _) => row is null ? null : BuildRowTemplate(), supportsRecycling: true);
        return list;
    }

    private static Control BuildRowTemplate()
    {
        // Cover-Frame 140x90 (Preview aus der ZIP extrahiert).
        var coverFrame = new Border
        {
            Width = 140, Height = 90,
            CornerRadius = new CornerRadius(6),
            ClipToBounds = true,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSurfaceBrush"),
        };
        var coverPanel = new Panel();
        var coverFallback = new TextBlock
        {
            Text = "📦",
            FontSize = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        coverFallback.Classes.Add("muted");
        coverPanel.Children.Add(coverFallback);
        var coverImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(ModRow.Preview)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        // Titel + INSTALLIERT-Badge
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.Title)));
        titleRow.Children.Add(title);

        var installedBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSuccessBrush"),
        };
        installedBadge.Child = new TextBlock
        {
            Text = "✓ INSTALLIERT",
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
        };
        installedBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(ModRow.IsAlreadyInstalled)));
        titleRow.Children.Add(installedBadge);

        // Meta: Author · vX.Y · Größe · FileName
        var meta = new TextBlock
        {
            FontSize = 11, Margin = new Thickness(0, 2, 0, 0),
        };
        meta.Classes.Add("muted");
        meta.Bind(TextBlock.TextProperty, new MultiBinding
        {
            Bindings =
            {
                new Binding(nameof(ModRow.Author)),
                new Binding(nameof(ModRow.Version)),
                new Binding(nameof(ModRow.Size)),
                new Binding(nameof(ModRow.FileName)),
            },
            Converter = new JoinConverter(),
        });

        var textStack = new StackPanel
        {
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { titleRow, meta },
        };

        // Row-Buttons rechts: Installieren (accent) + Löschen (danger)
        var installBtn = new Button { Content = "📥  Installieren" };
        installBtn.Classes.Add("accent");
        installBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(DownloadsViewModel.InstallRowCommand),
        });
        installBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var deleteBtn = new Button { Content = "🗑  Löschen" };
        deleteBtn.Classes.Add("danger");
        deleteBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(DownloadsViewModel.DeleteRowCommand),
        });
        deleteBtn.Bind(Button.CommandParameterProperty, new Binding("."));

        var actions = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { installBtn, deleteBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(coverFrame, 0);
        Grid.SetColumn(textStack, 1);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(coverFrame);
        grid.Children.Add(textStack);
        grid.Children.Add(actions);

        var card = new Border { Margin = new Thickness(0, 0, 0, 8), Child = grid };
        card.Classes.Add("card");
        return card;
    }

    private static Control WithDock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    private sealed class JoinConverter : Avalonia.Data.Converters.IMultiValueConverter
    {
        public object? Convert(System.Collections.Generic.IList<object?> values,
            System.Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var v in values)
                if (v is string s && !string.IsNullOrWhiteSpace(s)) parts.Add(s);
            return string.Join("  ·  ", parts);
        }
    }
}
