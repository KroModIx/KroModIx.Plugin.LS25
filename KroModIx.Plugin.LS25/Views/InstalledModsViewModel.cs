using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.LS25.Services;

namespace KroModIx.Plugin.LS25.Views;

public sealed partial class InstalledModsViewModel : ObservableObject
{
    private const string Language = "de";

    private readonly ModInstallService _installer;
    private readonly ModBackupService _backup;
    private readonly ModPreviewService _previews;
    private readonly ModHubService _hub;
    private readonly CatalogCache _cache;
    private readonly Ls25Paths _paths;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;
    private readonly InstalledUpdatesChecker _updatesChecker;

    public InstalledModsViewModel(ModInstallService installer, ModBackupService backup,
        ModPreviewService previews, ModHubService hub, CatalogCache cache,
        Ls25Paths paths, DownloadEventBus downloadBus, IHostServices host,
        InstalledUpdatesChecker updatesChecker)
    {
        _installer = installer;
        _backup = backup;
        _previews = previews;
        _hub = hub;
        _cache = cache;
        _paths = paths;
        _downloadBus = downloadBus;
        _host = host;
        _updatesChecker = updatesChecker;
        ModsDir = installer.ModsDir;
        InitEvents();
        RefreshCommand.Execute(null);

        // Auto-Refresh: sobald irgendwo im Plugin (Downloads-Tab, Drop, Update-
        // Aktion aus ModHub) ein Mod in den Mods-Ordner geschrieben wurde,
        // aktualisiert sich diese Liste automatisch — kein User-Klick auf
        // „Aktualisieren" nötig.
        _downloadBus.ModInstalled += (_, _) =>
            Dispatcher.UIThread.Post(() => Refresh());
    }

    public string ModsDir { get; }

    /// <summary>Kompletter Datenbestand — gefiltert in <see cref="Mods"/>
    /// per <see cref="ApplyFilter"/>. Refresh() füllt _allMods, ApplyFilter
    /// rendert Mods.</summary>
    private readonly List<ModRow> _allMods = new();
    public ObservableCollection<ModRow> Mods { get; } = new();

    /// <summary>Von der ListBox über TwoWay-Binding gefüllte Auswahl. Wird
    /// bei Bulk-Aktionen (Aktivieren/Deaktivieren/Deinstallieren mehrere
    /// Mods gleichzeitig) verwendet. Die Single-Selected-Property bleibt
    /// für Tastatur-Fokus und Row-basierte Commands.</summary>
    public ObservableCollection<ModRow> SelectedRows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(SelectedCountLabel))]
    private ModRow? _selected;

    public bool HasSelection => Selected is not null;
    public bool HasMultiSelection => SelectedRows.Count > 1;
    public string SelectedCountLabel =>
        SelectedRows.Count > 1 ? $"{SelectedRows.Count} ausgewählt" : "";

    [ObservableProperty]
    private bool _isCheckingUpdates;

    [ObservableProperty]
    private string _summary = "";

    /// <summary>Volltext-Filter über Titel/Autor/Dateiname.</summary>
    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Filter-Toggle: nur Mods anzeigen bei denen ein Update
    /// verfügbar ist (nach CheckUpdatesAsync). Analog Standalone-Filter.</summary>
    [ObservableProperty]
    private bool _onlyWithUpdate;

    partial void OnSelectedChanged(ModRow? value) => OnPropertyChanged(nameof(HasSelection));
    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnOnlyWithUpdateChanged(bool value) => ApplyFilter();

    public InstalledModsViewModel InitEvents()
    {
        SelectedRows.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasMultiSelection));
            OnPropertyChanged(nameof(SelectedCountLabel));
        };
        return this;
    }

    /// <summary>Sync-Wrapper der die eigentliche Arbeit off-thread startet.
    /// <see cref="ModInstallService.ListInstalled"/> öffnet jede Mod-ZIP für
    /// den Metadata- + DDS-Preview-Read — bei 60+ Mods sind das schnell mal
    /// 30 s Blockade wenn man das auf dem UI-Thread laufen lässt (Startup-
    /// Freeze). Deshalb: ListInstalled in Task.Run, Ergebnis-Materialisierung
    /// in Mods+Summary zurück auf UI-Thread (weil Bindings PropertyChanged
    /// nur vom UI-Thread aus feuern dürfen).</summary>
    [RelayCommand]
    private void Refresh()
    {
        Summary = "Mod-Liste wird gelesen …";
        _ = Task.Run(async () =>
        {
            List<InstalledMod>? mods = null;
            string? error = null;
            try { mods = _installer.ListInstalled().ToList(); }
            catch (Exception ex) { error = ex.Message; _host.Logger.Warn(ex, "LS25: Mod-Liste konnte nicht geladen werden"); }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _allMods.Clear();
                if (mods is not null)
                {
                    foreach (var m in mods
                                 .OrderByDescending(m => m.IsEnabled)
                                 .ThenBy(m => m.Metadata?.Title ?? m.FileName, StringComparer.CurrentCultureIgnoreCase))
                        _allMods.Add(new ModRow(m));

                    var enabled = _allMods.Count(r => r.Source.IsEnabled);
                    var total = _allMods.Count;
                    var totalBytes = _allMods.Where(r => r.Source.IsEnabled).Sum(r => r.Source.FileSizeBytes);
                    Summary = total == 0
                        ? "Keine Mods im Mods-Ordner."
                        : $"{enabled} aktiv / {total} total · {FormatBytes(totalBytes)}";
                }
                else
                {
                    Summary = $"Fehler beim Lesen des Mods-Ordners: {error}";
                }

                ApplyFilter();
                _ = LoadPreviewsAsync(_allMods.ToArray());
            });
        });
    }

    /// <summary>Filtert <see cref="_allMods"/> nach <see cref="SearchText"/>
    /// und <see cref="OnlyWithUpdate"/> in <see cref="Mods"/>. Wird bei
    /// jedem Filter-Change und Refresh() aufgerufen.</summary>
    private void ApplyFilter()
    {
        var q = SearchText?.Trim() ?? "";
        Mods.Clear();
        foreach (var row in _allMods)
        {
            if (OnlyWithUpdate && !row.HasUpdate) continue;
            if (q.Length > 0)
            {
                bool hit = row.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || row.FileName.Contains(q, StringComparison.OrdinalIgnoreCase);
                if (!hit) continue;
            }
            Mods.Add(row);
        }
    }

    private async Task LoadPreviewsAsync(ModRow[] rows)
    {
        foreach (var row in rows)
        {
            try
            {
                var path = await _previews.GetOrExtractInstalledPreviewAsync(row.Source.FilePath);
                if (path is null || !File.Exists(path)) continue;
                // Bitmap OFF-UI-Thread dekodieren (Skia auf Linux liest den
                // Stream ohne GL-Kontext). Nur die Property-Zuweisung MUSS auf
                // UI-Thread (weil der PropertyChanged-Event dort feuern muss).
                Bitmap? bmp = null;
                try
                {
                    bmp = await Task.Run(() =>
                    {
                        using var s = File.OpenRead(path);
                        return new Bitmap(s);
                    });
                }
                catch (Exception ex)
                {
                    _host.Logger.Debug(ex, "Preview-Bitmap-Decode fehlgeschlagen: {p}", path);
                    continue;
                }
                await Dispatcher.UIThread.InvokeAsync(() => row.Preview = bmp);
            }
            catch (Exception ex)
            {
                _host.Logger.Debug(ex, "Preview-Extraction fehlgeschlagen: {p}", row.Source.FilePath);
            }
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToggleEnabled() => ToggleEnabledRow(Selected);

    [RelayCommand]
    private void ToggleEnabledRow(ModRow? row)
    {
        if (row is null) return;
        try
        {
            var updated = _installer.SetEnabled(row.Source, !row.Source.IsEnabled);
            _host.Notifications.Notify(
                $"Mod {(updated.IsEnabled ? "aktiviert" : "deaktiviert")}: {updated.FileName}",
                NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Toggle fehlgeschlagen");
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task UninstallAsync() => await UninstallRowAsync(Selected);

    /// <summary>Aktiviert bzw. deaktiviert alle in <see cref="SelectedRows"/>.
    /// Wenn gemischter Zustand: alle werden AKTIVIERT (Standalone-Muster).
    /// Sonst wird der Zustand invertiert.</summary>
    [RelayCommand]
    private void ToggleEnabledBulk()
    {
        if (SelectedRows.Count == 0) return;
        var rows = SelectedRows.ToList(); // Snapshot — Refresh() gleich rebuild
        bool allEnabled = rows.All(r => r.Source.IsEnabled);
        bool target = !allEnabled; // gemischt → aktivieren, alle-aktiv → deaktivieren
        int done = 0;
        foreach (var r in rows)
        {
            try
            {
                if (r.Source.IsEnabled != target)
                    _installer.SetEnabled(r.Source, target);
                done++;
            }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Toggle für {F}", r.FileName); }
        }
        _host.Notifications.Notify(
            $"{done} Mod(s) {(target ? "aktiviert" : "deaktiviert")}.",
            NotificationLevel.Success);
        Refresh();
    }

    /// <summary>Deinstalliert alle <see cref="SelectedRows"/> mit einem
    /// Sammel-Confirm-Dialog.</summary>
    [RelayCommand]
    private async Task UninstallBulkAsync()
    {
        if (SelectedRows.Count == 0) return;
        var rows = SelectedRows.ToList();
        bool ok = await _host.Dialogs.ConfirmAsync(
            "Mods deinstallieren",
            $"{rows.Count} Mod(s) wirklich löschen?\n\n" +
            string.Join("\n", rows.Take(10).Select(r => "• " + r.FileName)) +
            (rows.Count > 10 ? $"\n… und {rows.Count - 10} weitere" : ""),
            okLabel: "Löschen", cancelLabel: "Abbrechen");
        if (!ok) return;

        int done = 0;
        foreach (var r in rows)
        {
            try { _installer.Uninstall(r.Source); done++; }
            catch (Exception ex) { _host.Logger.Warn(ex, "Bulk-Uninstall für {F}", r.FileName); }
        }
        _host.Notifications.Notify($"{done} Mod(s) deinstalliert.", NotificationLevel.Success);
        Refresh();
    }

    [RelayCommand]
    private async Task UninstallRowAsync(ModRow? row)
    {
        if (row is null) return;
        bool ok = await _host.Dialogs.ConfirmAsync(
            "Mod deinstallieren",
            $"„{row.Source.FileName}“ wirklich löschen?",
            okLabel: "Löschen", cancelLabel: "Abbrechen");
        if (!ok) return;
        try
        {
            _installer.Uninstall(row.Source);
            _host.Notifications.Notify($"Deinstalliert: {row.Source.FileName}", NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Uninstall fehlgeschlagen");
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private async Task InstallFromFileAsync()
    {
        var picked = await _host.Dialogs.PickFileAsync(
            "Mod-ZIP wählen",
            ("LS25-Mod (.zip)", new[] { "*.zip" }));
        if (picked is null) return;
        try
        {
            var installed = _installer.Install(picked, overwrite: false);
            _host.Notifications.Notify($"Installiert: {installed.FileName}", NotificationLevel.Success);
            _downloadBus.RaiseModInstalled(installed.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Install fehlgeschlagen");
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private void OpenModsFolder() => _host.Shell.OpenDirectory(ModsDir);

    /// <summary>Öffnet den GIANTS-Detail-Dialog für die installierte Row.
    /// Voraussetzung: Katalog-Cache ist vorhanden (User war schon mal im
    /// ModHub-Tab) UND der Fuzzy-Filename-Match findet einen Katalog-Eintrag.
    /// Sonst Info-Toast statt lautlos zu failen.</summary>
    [RelayCommand]
    private void ShowDetail(ModRow? row)
    {
        if (row is null) return;
        var snapshot = _cache.Load(Language);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            _host.Notifications.Notify(
                "Kein Katalog-Cache vorhanden. Erst ModHub-Tab öffnen, damit der Katalog geladen wird.",
                NotificationLevel.Warning);
            return;
        }
        var entry = LookupCatalogEntry(snapshot.Entries, row.FileName);
        if (entry is null)
        {
            _host.Notifications.Notify(
                $"Kein Katalog-Eintrag für „{row.Title}\" gefunden (Fuzzy-Match hat nicht gegriffen).",
                NotificationLevel.Info);
            return;
        }
        var modId = ExtractModIdFromUrl(entry.DetailUrl);
        if (modId is null)
        {
            _host.Notifications.Notify(
                $"Katalog-Eintrag hat keine mod_id: {entry.DetailUrl}",
                NotificationLevel.Warning);
            return;
        }

        var catalogRow = new CatalogRow(entry);
        var vm = new ModDetailViewModel(modId.Value, catalogRow, _hub, _previews, _downloadBus, _host);
        var window = new ModDetailWindow { DataContext = vm };
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) window.Show(owner); else window.Show();
    }

    /// <summary>Wird vom Drag&amp;Drop-Handler in der View aufgerufen — pro
    /// gedropter .zip-Datei einmal. Fehler landen im Log + Notify; die View
    /// ruft am Ende einmal Refresh() für den Gesamtstand auf.</summary>
    public void InstallDroppedZip(string zipPath)
    {
        try
        {
            var installed = _installer.Install(zipPath, overwrite: false);
            _host.Notifications.Notify($"Installiert (Drop): {installed.FileName}",
                NotificationLevel.Success);
            _downloadBus.RaiseModInstalled(installed.FileName);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Drop-Install fehlgeschlagen für {P}", zipPath);
            _host.Notifications.Notify(
                $"Drop-Install fehlgeschlagen ({System.IO.Path.GetFileName(zipPath)}): {ex.Message}",
                NotificationLevel.Error);
        }
    }

    /// <summary>Delegiert an <see cref="InstalledUpdatesChecker"/>. Der schreibt
    /// nach dem Run in den <see cref="InstalledUpdatesTracker"/> — Sidebar-
    /// Kachel-Badge wird beim nächsten Poll aktualisiert.</summary>
    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        if (IsCheckingUpdates) return;
        IsCheckingUpdates = true;
        try
        {
            var snapshot = _cache.Load(Language);
            if (snapshot is null || snapshot.Entries.Count == 0)
            {
                _host.Notifications.Notify(
                    "Kein Katalog-Cache vorhanden. Erst ModHub-Tab öffnen, damit der Katalog geladen wird.",
                    NotificationLevel.Warning);
                return;
            }

            var updated = await _updatesChecker.CheckAsync(
                onUpdateFound: (fileName, oldVer, newVer) =>
                {
                    var row = Mods.FirstOrDefault(r => r.FileName == fileName);
                    if (row is not null)
                        Dispatcher.UIThread.Post(() => row.SetUpdateAvailable(newVer));
                },
                onProgress: msg => Summary = msg);
            Summary = updated > 0
                ? $"Updates gefunden: {updated} Mod(s)."
                : "Keine Updates.";
            _host.Notifications.Notify(Summary,
                updated > 0 ? NotificationLevel.Success : NotificationLevel.Info);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Update-Prüfung fehlgeschlagen");
            _host.Notifications.Notify($"Update-Prüfung: {ex.Message}", NotificationLevel.Error);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    /// <summary>Führt das Update aus: lädt neue Version, deinstalliert die alte,
    /// installiert die neue, überträgt Enabled-State. Voraussetzung: <see cref="ModRow.HasUpdate"/>
    /// ist true (per <see cref="CheckUpdatesAsync"/> gesetzt) und Katalog-Entry
    /// findet sich noch.</summary>
    [RelayCommand]
    private async Task UpdateModAsync(ModRow? row)
    {
        if (row is null || !row.HasUpdate) return;

        var snapshot = _cache.Load(Language);
        if (snapshot is null) return;
        var catalogEntry = LookupCatalogEntry(snapshot.Entries, row.FileName);
        if (catalogEntry is null)
        {
            _host.Notifications.Notify("Katalog-Eintrag für Update nicht mehr gefunden.", NotificationLevel.Warning);
            return;
        }
        var modId = ExtractModIdFromUrl(catalogEntry.DetailUrl);
        if (modId is null) return;

        using var scope = _host.BeginProgress($"Update: {row.Title}");
        var progress = new Progress<ModDownloadProgress>(p =>
            scope.Report(p.Fraction ?? 0, p.FormatShort()));

        try
        {
            var wasEnabled = row.Source.IsEnabled;

            // 1. Neue Version in den Downloads-Ordner
            var result = await _hub.DownloadModAsync(modId.Value, Language, progress,
                default, catalogEntry.PreviewUrl);
            if (result is null) throw new InvalidOperationException("Download lieferte null");
            _downloadBus.RaiseDownloadsChanged(result.FileName);

            // 2. Alte Version aus dem Mod-Ordner entfernen
            await Task.Run(() => _installer.Uninstall(row.Source));

            // 3. Neue Version installieren (aus dem Downloads-Ordner)
            var newMod = await Task.Run(() => _installer.Install(result.TargetZipPath, overwrite: true));

            // 4. Enabled-State übertragen — war die alte deaktiviert, deaktivieren wir die neue ebenfalls.
            if (!wasEnabled)
                await Task.Run(() => _installer.SetEnabled(newMod, false));

            _host.Notifications.Notify($"Update installiert: {row.Title} → v{row.LatestVersion}",
                NotificationLevel.Success);
            _downloadBus.RaiseModInstalled(newMod.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Update-Install fehlgeschlagen für {Title}", row.Title);
            _host.Notifications.Notify($"Update-Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    /// <summary>Fuzzy-Match: normalisiere Filename + Katalog-Titel (nur
    /// Buchstaben/Ziffern, lowercase, ohne LS/FS-Präfixe). Enthält der eine den
    /// anderen als Teilstring, ist es ein Treffer. Analog zum LS-ModManager,
    /// funktioniert für die meisten Mod-Filenamen.</summary>
    private static ModHubEntry? LookupCatalogEntry(IReadOnlyList<ModHubEntry> catalog, string zipFileName)
    {
        var normalized = NormalizeForMatch(Path.GetFileNameWithoutExtension(zipFileName));
        if (normalized.Length < 3) return null;
        foreach (var e in catalog)
        {
            var titleNorm = NormalizeForMatch(e.Title);
            if (titleNorm.Length < 3) continue;
            if (normalized.Contains(titleNorm) || titleNorm.Contains(normalized))
                return e;
        }
        return null;
    }

    private static string NormalizeForMatch(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        var result = sb.ToString();
        foreach (var prefix in new[] { "fs25", "fs22", "ls25", "ls22" })
            if (result.StartsWith(prefix)) result = result.Substring(prefix.Length);
        // .disabled kann noch dranhängen wenn der Filename via ZipFileName rein kommt.
        if (result.EndsWith("disabled")) result = result.Substring(0, result.Length - "disabled".Length);
        return result;
    }

    private static int? ExtractModIdFromUrl(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    private static bool IsVersionNewer(string catalogVersion, string installedVersion)
    {
        if (!Version.TryParse(catalogVersion.Trim(), out var cat)) return false;
        if (!Version.TryParse(installedVersion.Trim(), out var inst)) return false;
        return cat > inst;
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        if (Mods.Count == 0)
        {
            _host.Notifications.Notify("Keine Mods vorhanden — nichts zu sichern.", NotificationLevel.Warning);
            return;
        }
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
        var target = Path.Combine(_paths.BackupsDir, $"ls25-backup-{timestamp}.zip");
        using var scope = _host.BeginProgress("Backup erstellen …");
        var progress = new Progress<BackupProgress>(p =>
            scope.Report(p.Fraction, $"{p.Current}/{p.Total} · {p.CurrentFileName}"));
        try
        {
            var result = await _backup.CreateBackupAsync(target, progress);
            _host.Notifications.Notify(
                $"Backup: {result.ModCount} Mods · {FormatBytes(result.FileSizeBytes)} → {Path.GetFileName(result.FilePath)}",
                NotificationLevel.Success);
            _host.Shell.OpenDirectory(_paths.BackupsDir);
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Backup fehlgeschlagen");
            _host.Notifications.Notify($"Backup-Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync()
    {
        var picked = await _host.Dialogs.PickFileAsync(
            "Backup-ZIP wählen",
            ("LS25-Backup (.zip)", new[] { "*.zip" }));
        if (picked is null) return;

        // Preview: Manifest zeigen bevor der Restore läuft.
        BackupManifest manifest;
        try { manifest = ModBackupService.ReadManifest(picked); }
        catch (Exception ex)
        {
            _host.Notifications.Notify($"Backup ungültig: {ex.Message}", NotificationLevel.Error);
            return;
        }

        var confirm = await _host.Dialogs.ConfirmAsync(
            "Backup wiederherstellen",
            $"Backup vom {manifest.CreatedUtc.ToLocalTime():g} · {manifest.Mods.Count} Mods.\n" +
            "Vorhandene Mod-ZIPs mit gleichem Namen werden überschrieben.\nFortfahren?",
            okLabel: "Wiederherstellen", cancelLabel: "Abbrechen");
        if (!confirm) return;

        using var scope = _host.BeginProgress("Backup wiederherstellen …");
        var progress = new Progress<BackupProgress>(p =>
            scope.Report(p.Fraction, $"{p.Current}/{p.Total} · {p.CurrentFileName}"));
        try
        {
            var result = await _backup.RestoreBackupAsync(picked, progress);
            _host.Notifications.Notify(
                $"Restore: {result.RestoredCount} wiederhergestellt, {result.SkippedCount} übersprungen.",
                NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25: Restore fehlgeschlagen");
            _host.Notifications.Notify($"Restore-Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        double kb = bytes / 1024.0;
        if (kb < 1024) return $"{kb:F1} KB";
        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:F1} MB";
        double gb = mb / 1024.0;
        return $"{gb:F2} GB";
    }
}

public sealed partial class ModRow : ObservableObject
{
    public InstalledMod Source { get; }
    public ModRow(InstalledMod source) => Source = source;

    public string Title => Source.Metadata?.Title ?? Source.FileName;
    public string Author => Source.Metadata?.Author ?? "";
    public string Version => Source.Metadata?.Version ?? "";
    public string Description => Source.Metadata?.Description ?? "";
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    public string Size => FormatBytes(Source.FileSizeBytes);
    public bool IsEnabled => Source.IsEnabled;
    public string StateLabel => Source.IsEnabled ? "aktiv" : "inaktiv";
    public string FileName => Source.FileName;
    public string? ErrorText => Source.ReadError;

    [ObservableProperty]
    private Bitmap? _preview;

    /// <summary>Nur im Downloads-Tab benutzt: markiert Rows deren Filename
    /// bereits als installierter Mod existiert (Fuzzy-Filename-Match).</summary>
    [ObservableProperty]
    private bool _isAlreadyInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private bool _hasUpdate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateBadgeText))]
    private string? _latestVersion;

    public string UpdateBadgeText =>
        HasUpdate && LatestVersion is not null ? $"⬆ Update v{LatestVersion}" : "";

    public void SetUpdateAvailable(string catalogVersion)
    {
        LatestVersion = catalogVersion;
        HasUpdate = true;
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F0} KB";
        return $"{bytes / 1024.0 / 1024.0:F1} MB";
    }
}
