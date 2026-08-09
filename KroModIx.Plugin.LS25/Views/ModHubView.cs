using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using KroModIx.Plugin.LS25.Services;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// ModHub-Katalog-Tab im Kroste-Card-Look — nahe am standalone LS-ModManager.
/// Wichtig: keine hartkodierten Farben mehr, alles über DynamicResource
/// (KrosteCardBrush, KrosteBorderBrush, KrosteAccent*, KrosteGold, KrosteMuted/
/// SecondaryText) und die Style-Klassen aus dem Host-App.axaml
/// (Border.card, Button.accent/.ghost, TextBlock.h2/.muted/.section-label,
/// Rectangle.divider-v).
/// </summary>
public sealed class ModHubView : UserControl
{
    public ModHubView()
    {
        Content = new DockPanel
        {
            Margin = new Thickness(20, 16, 20, 14),
            Children =
            {
                WithDock(BuildToolbar(),   Dock.Top),
                WithDock(BuildHint(),      Dock.Top),
                WithDock(BuildSummary(),   Dock.Top),
                WithDock(BuildStatus(),    Dock.Bottom),
                BuildList(),
            },
        };
    }

    private static Control BuildToolbar()
    {
        var sourceBox = new ComboBox
        {
            Width = 170,
            [!ComboBox.PlaceholderTextProperty] = new Binding { Source = "Alle Quellen" },
            DisplayMemberBinding = new Binding(nameof(SourceFilterOption.Label)),
        };
        sourceBox.Bind(ComboBox.ItemsSourceProperty, new Binding(nameof(ModHubViewModel.Sources)));
        sourceBox.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(ModHubViewModel.SelectedSource))
        { Mode = BindingMode.TwoWay });

        var categoryBox = new ComboBox
        {
            Width = 220,
            [!ComboBox.PlaceholderTextProperty] = new Binding { Source = "Alle Kategorien" },
            DisplayMemberBinding = new Binding(nameof(ModHubCategory.Label)),
        };
        categoryBox.Bind(ComboBox.ItemsSourceProperty, new Binding(nameof(ModHubViewModel.Categories)));
        categoryBox.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(ModHubViewModel.SelectedCategory))
        { Mode = BindingMode.TwoWay });

        var searchBox = new TextBox
        {
            Width = 240,
            [!TextBox.PlaceholderTextProperty] = new Binding { Source = "Titel/Autor/Kategorie …" },
        };
        searchBox.Bind(TextBox.TextProperty, new Binding(nameof(ModHubViewModel.SearchText))
        { Mode = BindingMode.TwoWay });

        var refreshBtn = new Button { Content = "↺  Katalog neu laden" };
        refreshBtn.Classes.Add("ghost");
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(ModHubViewModel.RefreshCatalogCommand)));

        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { sourceBox, categoryBox, searchBox, refreshBtn },
        };

        // Rechts: Sortier-Combo. Options aus ModHubViewModel.SortOptions
        // (Standard, NEU zuerst, Name, Autor, Kategorie).
        var sortLabel = new TextBlock
        {
            Text = "Sortierung:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };
        sortLabel.Classes.Add("muted");
        var sortBox = new ComboBox
        {
            Width = 170,
            DisplayMemberBinding = new Binding(nameof(CatalogSortOption.Label)),
        };
        sortBox.Bind(ComboBox.ItemsSourceProperty, new Binding(nameof(ModHubViewModel.SortOptions)));
        sortBox.Bind(ComboBox.SelectedItemProperty, new Binding(nameof(ModHubViewModel.SelectedSort))
        { Mode = BindingMode.TwoWay });

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { sortLabel, sortBox },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    private static Control BuildHint()
    {
        var hint = new TextBlock
        {
            Text = "GIANTS: In-App-Download · Hof Hirschfeld & modhoster: Detail-Klick öffnet die Seite im Browser (Consent-Overlay bzw. Login-Pflicht).",
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        hint.Classes.Add("muted");
        return hint;
    }

    private static Control BuildSummary()
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var label = new TextBlock
        {
            Text = "🤖  KI-Zusammenfassung",
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.Classes.Add("h2");
        var closeBtn = new Button { Content = "✕", Padding = new Thickness(8, 2) };
        closeBtn.Classes.Add("ghost");
        closeBtn.Bind(Button.CommandProperty, new Binding(nameof(ModHubViewModel.CloseSummaryCommand)));
        header.Children.Add(label);
        header.Children.Add(closeBtn);

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0),
            LineHeight = 20,
        };
        text.Bind(TextBlock.TextProperty, new Binding(nameof(ModHubViewModel.SummaryText)));

        var card = new Border
        {
            Margin = new Thickness(0, 0, 0, 10),
            Child = new StackPanel { Spacing = 4, Children = { header, text } },
        };
        card.Classes.Add("card");
        card.Bind(Border.IsVisibleProperty, new Binding(nameof(ModHubViewModel.SummaryVisible)));
        return card;
    }

    private static Control BuildStatus()
    {
        var status = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
        status.Classes.Add("muted");
        status.Bind(TextBlock.TextProperty, new Binding(nameof(ModHubViewModel.Status)));
        return status;
    }

    private static Control BuildList()
    {
        var list = new ListBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(ModHubViewModel.Rows)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(ModHubViewModel.Selected))
        { Mode = BindingMode.TwoWay });
        list.ItemTemplate = new FuncDataTemplate<CatalogRow>((row, _) => row is null ? null : BuildRowTemplate(), supportsRecycling: true);

        // Doppelklick auf eine Katalog-Row → Details-Fenster (nur GIANTS,
        // sonst „Detail im Browser"-Fallback). Analog Standalone-Muster.
        list.DoubleTapped += (_, _) =>
        {
            if (list.DataContext is ModHubViewModel vm && vm.Selected is not null)
                vm.ShowDetailForRowCommand.Execute(vm.Selected);
        };
        return list;
    }

    private static Control BuildRowTemplate()
    {
        // Cover-Frame links (140x90) — LS-ModManager-Format.
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
            Text = "🌐",
            FontSize = 30,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        coverFallback.Classes.Add("muted");
        coverPanel.Children.Add(coverFallback);

        // Explizit Stretch/Stretch — sonst rendert das Image auf einigen
        // Systemen in Default-Größe (0×0) und das Cover bleibt unsichtbar
        // trotz gesetzter Source-Bitmap.
        var coverImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        coverImage.Bind(Image.SourceProperty, new Binding(nameof(CatalogRow.Cover)));
        coverPanel.Children.Add(coverImage);
        coverFrame.Child = coverPanel;

        // Titel + Badges
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(CatalogRow.Title)));
        titleRow.Children.Add(title);
        titleRow.Children.Add(MakeBadge(new Binding(nameof(CatalogRow.SourceLabel)),
            "KrosteAccentSoftBrush", "KrosteSecondaryTextBrush", null));
        // ✓ INSTALLIERT (grün mit weißem Text) — Fuzzy-Match Titel ↔ Filename.
        // Analog Downloads-Tab-Badge; MakeBadge braucht IBrush-Overload weil
        // Weiß nicht als Kroste-Resource-Key existiert.
        titleRow.Children.Add(MakeBadgeSolid("✓ INSTALLIERT",
            "KrosteSuccessBrush", Brushes.White, new Binding(nameof(CatalogRow.IsInstalled))));
        titleRow.Children.Add(MakeBadge(new Binding { Source = "⭐ EMPFOHLEN" },
            "KrosteGoldBrush", null, new Binding(nameof(CatalogRow.IsFeatured))));
        titleRow.Children.Add(MakeBadge(new Binding { Source = "NEU" },
            "KrosteGoldBrush", null, new Binding(nameof(CatalogRow.IsNew))));

        // Author · Category
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        var author = new TextBlock();
        author.Classes.Add("muted");
        author.Bind(TextBlock.TextProperty, new Binding(nameof(CatalogRow.Author)));
        var sep = new TextBlock { Text = "·" };
        sep.Classes.Add("muted");
        var category = new TextBlock();
        category.Classes.Add("muted");
        category.Bind(TextBlock.TextProperty, new Binding(nameof(CatalogRow.Category)));
        meta.Children.Add(author);
        meta.Children.Add(sep);
        meta.Children.Add(category);

        var textStack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { titleRow, meta },
        };

        // Aktions-Buttons rechts
        var actions = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Orientation = Orientation.Vertical,
        };
        var downloadBtn = new Button { Content = "⬇  Herunterladen" };
        downloadBtn.Classes.Add("accent");
        downloadBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(ModHubViewModel.DownloadFromRowCommand),
        });
        downloadBtn.CommandParameter = null;
        downloadBtn.Bind(Button.CommandParameterProperty, new Binding("."));
        downloadBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(CatalogRow.CanInAppDownload)));

        var browserBtn = new Button { Content = "🌐  Im Browser" };
        browserBtn.Classes.Add("accent");
        browserBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(ModHubViewModel.OpenRowInBrowserCommand),
        });
        browserBtn.Bind(Button.CommandParameterProperty, new Binding("."));
        browserBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(CatalogRow.NeedsBrowser)));

        var detailsBtn = new Button { Content = "👁  Details" };
        detailsBtn.Classes.Add("ghost");
        detailsBtn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + nameof(ModHubViewModel.ShowDetailForRowCommand),
        });
        detailsBtn.Bind(Button.CommandParameterProperty, new Binding("."));
        detailsBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(CatalogRow.CanInAppDownload)));

        actions.Children.Add(downloadBtn);
        actions.Children.Add(browserBtn);
        actions.Children.Add(detailsBtn);

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

    private static Border MakeBadge(Binding textBinding, string bgResourceKey,
        string? fgResourceKey, Binding? visibleBinding)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension(bgResourceKey),
        };
        var tb = new TextBlock
        {
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
        };
        if (fgResourceKey is not null)
            tb[!TextBlock.ForegroundProperty] = new DynamicResourceExtension(fgResourceKey);
        else
            tb.Foreground = Brushes.Black;
        tb.Bind(TextBlock.TextProperty, textBinding);
        border.Child = tb;
        if (visibleBinding is not null) border.Bind(Border.IsVisibleProperty, visibleBinding);
        return border;
    }

    /// <summary>Wie <see cref="MakeBadge"/>, aber mit festem Text und
    /// direktem Brush als Foreground (für Weiß auf grünen Success-Badges —
    /// „Weiß" gibt es nicht als Kroste-Resource-Key).</summary>
    private static Border MakeBadgeSolid(string text, string bgResourceKey,
        IBrush foreground, Binding? visibleBinding)
    {
        var border = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension(bgResourceKey),
        };
        border.Child = new TextBlock
        {
            Text = text,
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
        };
        if (visibleBinding is not null) border.Bind(Border.IsVisibleProperty, visibleBinding);
        return border;
    }

    private static Control WithDock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }
}
