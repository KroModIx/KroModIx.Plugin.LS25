using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// Installiert-Tab im Kroste-Card-Look. Toolbar in Sektionen SPIEL / INSTALLATION
/// / SYSTEM. Filter-Zeile mit Volltextsuche + „nur mit Update"-Toggle. Multi-
/// Select via Ctrl+Klick/Shift+Klick, Kontextmenü + Del-Key + F5 + Ctrl+F.
/// Drag&Drop von .zip-Files aufs Fenster installiert die Mods direkt.
/// </summary>
public sealed class InstalledModsView : UserControl
{
    private ListBox? _list;
    private TextBox? _searchBox;

    public InstalledModsView()
    {
        // Keyboard-Shortcuts + Drag&Drop auf dem UserControl-Root:
        // F5 = Refresh, Ctrl+F = Search fokussieren, Del = Bulk-Uninstall.
        Focusable = true;
        KeyDown += OnKeyDown;
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        DragDrop.SetAllowDrop(this, true);

        _searchBox = BuildSearchBox();
        _list = BuildList();

        Content = new DockPanel
        {
            Margin = new Thickness(20, 16, 20, 14),
            Children =
            {
                WithDock(BuildToolbar(), Dock.Top),
                WithDock(BuildFilterRow(), Dock.Top),
                WithDock(BuildPathLabel(), Dock.Top),
                WithDock(BuildSummary(), Dock.Bottom),
                _list,
            },
        };
    }

    private static Control BuildToolbar()
    {
        // MOD-UPDATES + Bulk-Aktionen für ausgewählte Rows.
        var updatesBtn = new Button { Content = "🔄  Updates prüfen" };
        updatesBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.CheckUpdatesCommand)));

        var installBtn = new Button { Content = "📁  ZIP installieren…" };
        installBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.InstallFromFileCommand)));
        var refreshBtn = new Button { Content = "↺  Aktualisieren" };
        refreshBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.RefreshCommand)));

        // Bulk-Buttons — nur aktiv/klar sichtbar wenn > 1 Row selektiert.
        // Bei einer Selektion greifen weiter die Row-Buttons rechts an der Card.
        var toggleBulkBtn = new Button { Content = "🔀  Aktiv/Inaktiv" };
        toggleBulkBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.ToggleEnabledBulkCommand)));
        toggleBulkBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(InstalledModsViewModel.HasMultiSelection)));

        var uninstallBulkBtn = new Button { Content = "🗑  Auswahl deinstallieren" };
        uninstallBulkBtn.Classes.Add("danger");
        uninstallBulkBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.UninstallBulkCommand)));
        uninstallBulkBtn.Bind(Button.IsEnabledProperty, new Binding(nameof(InstalledModsViewModel.HasMultiSelection)));

        // SYSTEM
        var openFolderBtn = new Button { Content = "📂  Mod-Ordner" };
        openFolderBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.OpenModsFolderCommand)));
        var backupBtn = new Button { Content = "💾  Backup" };
        backupBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.CreateBackupCommand)));
        var restoreBtn = new Button { Content = "♻  Restore" };
        restoreBtn.Bind(Button.CommandProperty, new Binding(nameof(InstalledModsViewModel.RestoreBackupCommand)));

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(0, 0, 0, 10),
        };
        toolbar.Children.Add(updatesBtn);
        toolbar.Children.Add(NewDivider());
        toolbar.Children.Add(installBtn);
        toolbar.Children.Add(refreshBtn);
        toolbar.Children.Add(toggleBulkBtn);
        toolbar.Children.Add(uninstallBulkBtn);
        toolbar.Children.Add(NewDivider());
        toolbar.Children.Add(openFolderBtn);
        toolbar.Children.Add(backupBtn);
        toolbar.Children.Add(restoreBtn);
        return toolbar;
    }

    private static TextBox BuildSearchBox()
    {
        // Filter-Zeile: Suchfeld + „Nur mit Update"-Toggle + Auswahl-Zähler.
        var box = new TextBox
        {
            [!TextBox.PlaceholderTextProperty] = new Binding
            {
                Source = "Installierte Mods filtern (Titel/Autor/Dateiname) …",
            },
            Margin = new Thickness(0, 0, 8, 0),
        };
        box.Bind(TextBox.TextProperty, new Binding(nameof(InstalledModsViewModel.SearchText))
        { Mode = BindingMode.TwoWay });
        return box;
    }

    private Control BuildFilterRow()
    {
        var onlyUpdate = new ToggleButton { Content = "⬆  Nur mit Update" };
        onlyUpdate.Bind(ToggleButton.IsCheckedProperty, new Binding(nameof(InstalledModsViewModel.OnlyWithUpdate))
        { Mode = BindingMode.TwoWay });

        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        count.Classes.Add("muted");
        count.Bind(TextBlock.TextProperty, new Binding(nameof(InstalledModsViewModel.SelectedCountLabel)));

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Margin = new Thickness(0, 0, 0, 10),
        };
        Grid.SetColumn(_searchBox!, 0);
        Grid.SetColumn(onlyUpdate, 1);
        Grid.SetColumn(count, 2);
        onlyUpdate.Margin = new Thickness(8, 0, 12, 0);
        grid.Children.Add(_searchBox!);
        grid.Children.Add(onlyUpdate);
        grid.Children.Add(count);
        return grid;
    }

    private static Control BuildPathLabel()
    {
        var text = new TextBlock
        {
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 8),
        };
        text.Classes.Add("muted");
        text.Bind(TextBlock.TextProperty, new Binding(nameof(InstalledModsViewModel.ModsDir))
        { StringFormat = "Mods-Ordner: {0}" });
        return text;
    }

    private static Control BuildSummary()
    {
        var summary = new TextBlock { Margin = new Thickness(0, 10, 0, 0) };
        summary.Classes.Add("muted");
        summary.Bind(TextBlock.TextProperty, new Binding(nameof(InstalledModsViewModel.Summary)));
        return summary;
    }

    private ListBox BuildList()
    {
        var list = new ListBox
        {
            // Multiple: Ctrl+Klick toggled einzelne, Shift+Klick spannt Bereich.
            SelectionMode = SelectionMode.Multiple,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
        };
        list.Bind(ListBox.ItemsSourceProperty, new Binding(nameof(InstalledModsViewModel.Mods)));
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(InstalledModsViewModel.Selected))
        { Mode = BindingMode.TwoWay });

        // SelectedRows via SelectionChanged-Event synchronisieren (kein direktes
        // Binding an SelectedItems weil das in Avalonia 12 ohne Custom-Behavior
        // nicht mit ObservableCollection<T> läuft).
        list.SelectionChanged += (_, _) =>
        {
            if (DataContext is not InstalledModsViewModel vm) return;
            vm.SelectedRows.Clear();
            foreach (var it in list.SelectedItems!)
                if (it is ModRow r) vm.SelectedRows.Add(r);
        };

        list.ItemTemplate = new FuncDataTemplate<ModRow>((row, _) => row is null ? null : BuildRowTemplate(), supportsRecycling: true);
        // Doppelklick auf Row öffnet Detail-Dialog (analog Icarus-Plugin).
        list.DoubleTapped += (_, _) =>
        {
            if (DataContext is InstalledModsViewModel vm && list.SelectedItem is ModRow row)
                vm.ShowDetailCommand.Execute(row);
        };
        return list;
    }

    private static Control BuildRowTemplate()
    {
        // Cover-Frame 140x90.
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
            Text = "🚜",
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

        // Titel + Zustands-Badge + Update-Badge
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var title = new TextBlock
        {
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.Title)));
        titleRow.Children.Add(title);

        var enabledBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteSuccessBrush"),
        };
        enabledBadge.Child = new TextBlock
        {
            Text = "aktiv", FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
        };
        enabledBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(ModRow.IsEnabled)));
        titleRow.Children.Add(enabledBadge);

        var updateBadge = new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1),
            VerticalAlignment = VerticalAlignment.Center,
            [!Border.BackgroundProperty] = new DynamicResourceExtension("KrosteGoldBrush"),
        };
        var updateBadgeText = new TextBlock
        {
            FontSize = 10, FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.Black,
        };
        updateBadgeText.Bind(TextBlock.TextProperty, new Binding(nameof(ModRow.UpdateBadgeText)));
        updateBadge.Child = updateBadgeText;
        updateBadge.Bind(Border.IsVisibleProperty, new Binding(nameof(ModRow.HasUpdate)));
        titleRow.Children.Add(updateBadge);

        // Meta
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        void AddMuted(Binding b, string? fmt = null)
        {
            var t = new TextBlock();
            t.Classes.Add("muted");
            if (fmt is not null) b.StringFormat = fmt;
            t.Bind(TextBlock.TextProperty, b);
            meta.Children.Add(t);
        }
        AddMuted(new Binding(nameof(ModRow.Author)));
        var sep1 = new TextBlock { Text = "·" }; sep1.Classes.Add("muted"); meta.Children.Add(sep1);
        AddMuted(new Binding(nameof(ModRow.Version)), "v{0}");
        var sep2 = new TextBlock { Text = "·" }; sep2.Classes.Add("muted"); meta.Children.Add(sep2);
        AddMuted(new Binding(nameof(ModRow.Size)));

        var textStack = new StackPanel
        {
            Spacing = 4,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
            Children = { titleRow, meta },
        };

        // Row-Aktionen rechts
        var updateBtn = new Button { Content = "⬆  Update" };
        updateBtn.Classes.Add("accent");
        BindRowCommand(updateBtn, nameof(InstalledModsViewModel.UpdateModCommand));
        updateBtn.Bind(Button.IsVisibleProperty, new Binding(nameof(ModRow.HasUpdate)));

        var detailBtn = new Button { Content = "🔍  Details" };
        BindRowCommand(detailBtn, nameof(InstalledModsViewModel.ShowDetailCommand));
        ToolTip.SetTip(detailBtn,
            "GIANTS-Detail-Dialog öffnen (Fuzzy-Match Filename → Katalog — Katalog muss vorher via ModHub-Tab geladen sein)");

        var toggleBtn = new Button { Content = "⏻  (De-)Aktivieren" };
        BindRowCommand(toggleBtn, nameof(InstalledModsViewModel.ToggleEnabledRowCommand));

        var uninstallBtn = new Button { Content = "🗑  Deinstallieren" };
        uninstallBtn.Classes.Add("danger");
        BindRowCommand(uninstallBtn, nameof(InstalledModsViewModel.UninstallRowCommand));

        var actions = new StackPanel
        {
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { updateBtn, detailBtn, toggleBtn, uninstallBtn },
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
        card.Bind(Border.OpacityProperty, new Binding(nameof(ModRow.IsEnabled))
        {
            Converter = new Avalonia.Data.Converters.FuncValueConverter<bool, double>(v => v ? 1.0 : 0.55),
        });

        // Kontextmenü pro Row — für Ein-Row-Aktionen. Bei Multi-Selection
        // wirken die Toolbar-Bulk-Buttons.
        var ctxMenu = new ContextMenu();
        var miToggle = new MenuItem { Header = "⏻  (De-)Aktivieren" };
        BindRowCommand(miToggle, nameof(InstalledModsViewModel.ToggleEnabledRowCommand));
        var miDetail = new MenuItem { Header = "🔍  Details" };
        BindRowCommand(miDetail, nameof(InstalledModsViewModel.ShowDetailCommand));
        var miUninstall = new MenuItem { Header = "🗑  Deinstallieren" };
        BindRowCommand(miUninstall, nameof(InstalledModsViewModel.UninstallRowCommand));
        var miUpdate = new MenuItem { Header = "⬆  Update" };
        BindRowCommand(miUpdate, nameof(InstalledModsViewModel.UpdateModCommand));
        miUpdate.Bind(MenuItem.IsVisibleProperty, new Binding(nameof(ModRow.HasUpdate)));
        ctxMenu.Items.Add(miUpdate);
        ctxMenu.Items.Add(miDetail);
        ctxMenu.Items.Add(miToggle);
        ctxMenu.Items.Add(new Separator());
        ctxMenu.Items.Add(miUninstall);
        card.ContextMenu = ctxMenu;

        return card;
    }

    private static Rectangle NewDivider()
    {
        var r = new Rectangle();
        r.Classes.Add("divider-v");
        return r;
    }

    private static void BindRowCommand(Button btn, string commandName)
    {
        btn.Bind(Button.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + commandName,
        });
        btn.Bind(Button.CommandParameterProperty, new Binding("."));
    }

    // Overload für MenuItem (nutzt gleiche RelativeSource-Kette).
    private static void BindRowCommand(MenuItem item, string commandName)
    {
        item.Bind(MenuItem.CommandProperty, new Binding
        {
            RelativeSource = new RelativeSource { Mode = RelativeSourceMode.FindAncestor, AncestorType = typeof(ListBox) },
            Path = "DataContext." + commandName,
        });
        item.Bind(MenuItem.CommandParameterProperty, new Binding("."));
    }

    private static Control WithDock(Control c, Dock dock)
    {
        DockPanel.SetDock(c, dock);
        return c;
    }

    // ---- Keyboard-Shortcuts ------------------------------------------------
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not InstalledModsViewModel vm) return;

        if (e.Key == Key.F5)
        {
            vm.RefreshCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control)
        {
            _searchBox?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            if (vm.SelectedRows.Count > 1)
                vm.UninstallBulkCommand.Execute(null);
            else if (vm.Selected is not null)
                vm.UninstallRowCommand.Execute(vm.Selected);
            e.Handled = true;
        }
    }

    // ---- Drag&Drop ---------------------------------------------------------
    private static void OnDragOver(object? sender, DragEventArgs e)
    {
        // Nur akzeptieren wenn die gedropten Files .zip-Endung haben.
        e.DragEffects = HasZipFiles(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not InstalledModsViewModel vm) return;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;
        int count = 0;
        foreach (var f in files)
        {
            var local = f.Path.LocalPath;
            if (!local.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                vm.InstallDroppedZip(local);
                count++;
            }
            catch { /* Notify läuft im VM */ }
        }
        if (count > 0) vm.RefreshCommand.Execute(null);
        e.Handled = true;
    }

    private static bool HasZipFiles(DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return false;
        return files.Any(f => f.Path.LocalPath.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase));
    }
}
