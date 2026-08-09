using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// Detail-Fenster für einen GIANTS-Mod. Custom-Chrome (borderless), Drag
/// per Titelleiste, Close-Button. Da das Plugin keine Host-ChromeWindow-Basis
/// hat, bauen wir das Chrome selbst — Avalonia 12: WindowDecorations.BorderOnly
/// + ExtendClientAreaToDecorationsHint = true. Der Look ist bewusst am
/// Kroste-Style orientiert (dunkler Titelbar-Farbton, akzentuierte Buttons).
/// </summary>
public sealed class ModDetailWindow : Window
{
    public ModDetailWindow()
    {
        Width = 820;
        Height = 720;
        MinWidth = 600;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(0x14, 0x17, 0x1C));
        // Custom-Chrome ohne native Titlebar — wir bauen sie selbst.
        // BorderOnly statt None, weil None die Resize-Griffe killt
        // (Kroste-Standard, per kroste-avalonia-Skill).
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        CanResize = true;

        Content = BuildContent();
    }

    private DockPanel BuildContent()
    {
        var titlebar = BuildTitleBar();
        var footer = BuildFooter();
        var body = BuildBody();

        var dp = new DockPanel();
        DockPanel.SetDock(titlebar, Dock.Top);
        DockPanel.SetDock(footer, Dock.Bottom);
        dp.Children.Add(titlebar);
        dp.Children.Add(footer);
        dp.Children.Add(body);
        return dp;
    }

    private Border BuildTitleBar()
    {
        var titleBlock = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        titleBlock.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.Title)));

        var closeBtn = new Button
        {
            Content = "✕",
            Width = 40, Height = 32,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        closeBtn.Click += (_, _) => Close();

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Height = 32,
        };
        Grid.SetColumn(titleBlock, 0);
        Grid.SetColumn(closeBtn, 1);
        grid.Children.Add(titleBlock);
        grid.Children.Add(closeBtn);

        var titlebar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x25)),
            Child = grid,
        };
        // Drag über Titel (nicht über den Close-Button — Grid-Item 0):
        titlebar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(titlebar).Properties.IsLeftButtonPressed)
                BeginMoveDrag(e);
        };
        return titlebar;
    }

    private Border BuildFooter()
    {
        var status = new TextBlock
        {
            Opacity = 0.7, FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
        };
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.StatusText)));

        var progress = new ProgressBar { IsIndeterminate = true, Width = 120, Margin = new Thickness(12, 0, 0, 0) };
        progress.Bind(ProgressBar.IsVisibleProperty, new Binding(nameof(ModDetailViewModel.IsLoading)));

        var openBtn = new Button { Content = "🌐  Detail im Browser", Margin = new Thickness(8, 0, 0, 0) };
        openBtn.Bind(Button.CommandProperty, new Binding(nameof(ModDetailViewModel.OpenInBrowserCommand)));

        var downloadBtn = new Button { Content = "⬇  Download", Margin = new Thickness(8, 0, 0, 0) };
        downloadBtn.Classes.Add("accent");
        downloadBtn.Bind(Button.CommandProperty, new Binding(nameof(ModDetailViewModel.DownloadCommand)));

        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Children = { progress, openBtn, downloadBtn },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(status, 0);
        Grid.SetColumn(stack, 1);
        grid.Children.Add(status);
        grid.Children.Add(stack);

        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1F, 0x25)),
            Padding = new Thickness(20, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x34, 0x3C)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = grid,
        };
    }

    private ScrollViewer BuildBody()
    {
        var stack = new StackPanel { Spacing = 14, Margin = new Thickness(20, 16) };

        // Header-Card mit Titel/Author/Meta
        stack.Children.Add(BuildHeaderCard());

        // KI-Toolbar (Zusammenfassen)
        stack.Children.Add(BuildAiToolbar());

        // Screenshots
        stack.Children.Add(BuildScreenshotsCard());

        // Beschreibung
        stack.Children.Add(BuildDescriptionCard());

        // KI-Summary (nur sichtbar wenn HasSummary)
        stack.Children.Add(BuildSummaryCard());

        return new ScrollViewer { Content = stack };
    }

    private static Border Card(params Control[] children)
    {
        var stack = new StackPanel { Spacing = 6 };
        foreach (var c in children) stack.Children.Add(c);
        return new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1F, 0x23, 0x28)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x34, 0x3C)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 12),
            Child = stack,
        };
    }

    private static Border BuildHeaderCard()
    {
        var title = new TextBlock
        {
            FontSize = 22, FontWeight = FontWeight.Bold, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE4, 0xE7, 0xEC)),
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.Title)));

        var subtitle = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var author = new TextBlock { Opacity = 0.7 };
        author.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.Author)));
        var category = new TextBlock { Opacity = 0.7 };
        category.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.Category))
        { StringFormat = "· {0}" });
        subtitle.Children.Add(author);
        subtitle.Children.Add(category);

        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Margin = new Thickness(0, 4, 0, 0) };
        void AddMetaChip(string prop, string format)
        {
            var chip = new TextBlock { Opacity = 0.65, FontSize = 12 };
            chip.Bind(TextBlock.TextProperty, new Binding(prop) { StringFormat = format });
            meta.Children.Add(chip);
        }
        AddMetaChip(nameof(ModDetailViewModel.Version), "v{0}");
        AddMetaChip(nameof(ModDetailViewModel.SizeText), "{0}");
        AddMetaChip(nameof(ModDetailViewModel.ReleaseDate), "{0}");
        AddMetaChip(nameof(ModDetailViewModel.Platform), "{0}");
        var rating = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB1, 0x4C)),
        };
        rating.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.Rating))
        { StringFormat = "⭐ {0}" });
        meta.Children.Add(rating);

        return Card(title, subtitle, meta);
    }

    private static Border BuildAiToolbar()
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var btn = new Button { Content = "🤖  Zusammenfassen (Ollama)" };
        btn.Bind(Button.CommandProperty, new Binding(nameof(ModDetailViewModel.SummarizeCommand)));
        var busy = new ProgressBar
        {
            IsIndeterminate = true, Width = 100, VerticalAlignment = VerticalAlignment.Center,
        };
        busy.Bind(ProgressBar.IsVisibleProperty, new Binding(nameof(ModDetailViewModel.SummaryBusy)));
        stack.Children.Add(btn);
        stack.Children.Add(busy);
        return Card(stack);
    }

    private static Border BuildScreenshotsCard()
    {
        var label = new TextBlock
        {
            Text = "Screenshots", FontWeight = FontWeight.SemiBold, Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4),
        };

        var items = new ItemsControl
        {
            ItemsPanel = new FuncTemplate<Panel?>(() =>
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 }),
            ItemTemplate = new FuncDataTemplate<ScreenshotItem>((item, _) =>
            {
                if (item is null) return null;
                var img = new Image
                {
                    Stretch = Stretch.UniformToFill,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                };
                img.Bind(Image.SourceProperty, new Binding(nameof(ScreenshotItem.Bitmap)));
                return new Border
                {
                    Width = 360, Height = 200,
                    Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2F, 0x38)),
                    CornerRadius = new CornerRadius(6),
                    ClipToBounds = true,
                    Child = img,
                };
            }, supportsRecycling: true),
        };
        items.Bind(ItemsControl.ItemsSourceProperty, new Binding(nameof(ModDetailViewModel.Screenshots)));

        var scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = items,
        };

        var card = Card(label, scroller);
        card.Bind(Border.IsVisibleProperty, new Binding($"{nameof(ModDetailViewModel.Screenshots)}.Count"));
        return card;
    }

    private static Border BuildDescriptionCard()
    {
        var label = new TextBlock
        {
            Text = "Beschreibung", FontWeight = FontWeight.SemiBold, Opacity = 0.9,
            Margin = new Thickness(0, 0, 0, 4),
        };
        var desc = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinHeight = 200,
        };
        desc.Bind(TextBox.TextProperty, new Binding(nameof(ModDetailViewModel.Description)) { Mode = BindingMode.OneWay });
        return Card(label, desc);
    }

    private static Border BuildSummaryCard()
    {
        var label = new TextBlock
        {
            Text = "🤖 KI-Zusammenfassung",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xB1, 0x4C)),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20,
            Opacity = 0.9,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(ModDetailViewModel.SummaryText)));
        var card = Card(label, text);
        card.Bind(Border.IsVisibleProperty, new Binding(nameof(ModDetailViewModel.HasSummary)));
        return card;
    }
}
