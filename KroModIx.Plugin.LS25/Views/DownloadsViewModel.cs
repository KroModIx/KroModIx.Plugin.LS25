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

/// <summary>
/// Downloads-Tab-VM. Zeigt bereits heruntergeladene ZIPs mit Preview aus dem
/// ZIP (analog Installiert-Tab, via <see cref="ModPreviewService"/>). Row-
/// basierte Commands (InstallRow, DeleteRow, ShowDetail). IsInstalled-Flag
/// per Filename-Fuzzy-Match gegen die installierten Mods → grünes „✓ INSTALLIERT"-Badge.
///
/// <para>Detail-Dialog aus dem Downloads-Tab: analog InstalledModsViewModel.ShowDetail
/// mit Fuzzy-Match auf Katalog-Cache → wenn erfolgreich, öffnet ModDetailWindow
/// mit Screenshots + KI-Zusammenfassung + Download-Button. Braucht den
/// ModHub-Katalog-Cache — bei leerem Cache: Info-Toast.</para>
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private const string Language = "de";

    private readonly ModInstallService _installer;
    private readonly ModPreviewService _previews;
    private readonly ModHubService _hub;
    private readonly CatalogCache _cache;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;

    public DownloadsViewModel(ModInstallService installer, ModPreviewService previews,
        ModHubService hub, CatalogCache cache,
        DownloadEventBus downloadBus, IHostServices host)
    {
        _installer = installer;
        _previews = previews;
        _hub = hub;
        _cache = cache;
        _downloadBus = downloadBus;
        _host = host;
        DownloadsDir = installer.DownloadsDir ?? "(nicht konfiguriert)";
        RefreshCommand.Execute(null);

        // Auto-Refresh: sobald der ModHub-Tab (oder ein anderer Tab) einen
        // Download in den Downloads-Ordner geschrieben hat, aktualisiert
        // sich diese Liste automatisch — ohne User-Klick auf Refresh.
        _downloadBus.DownloadsChanged += (_, fileName) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Refresh();
                _host.Notifications.Notify($"Downloads aktualisiert: {fileName}",
                    NotificationLevel.Info);
            });
        };
    }

    public string DownloadsDir { get; }

    public ObservableCollection<ModRow> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ModRow? _selected;

    public bool HasSelection => Selected is not null;

    [ObservableProperty]
    private string _summary = "";

    partial void OnSelectedChanged(ModRow? value) => OnPropertyChanged(nameof(HasSelection));

    /// <summary>Sync-Wrapper der die eigentliche Arbeit off-thread startet.
    /// <see cref="ModInstallService.ListDownloaded"/> und
    /// <see cref="ModInstallService.ListInstalled"/> öffnen intern jede ZIP
    /// für Metadata + DDS-Preview-Read — bei 60+ ZIPs sind das 30+ s sync
    /// auf UI-Thread. Deshalb Task.Run wie beim InstalledModsViewModel.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Summary = "Downloads werden gelesen …";
        _ = Task.Run(async () =>
        {
            List<InstalledMod>? downloaded = null;
            HashSet<string>? installedNames = null;
            string? error = null;
            try
            {
                downloaded = _installer.ListDownloaded()
                    .OrderByDescending(m => m.InstalledUtc).ToList();
                installedNames = new HashSet<string>(
                    _installer.ListInstalled().Select(m => Normalize(m.FileName)),
                    StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                _host.Logger.Warn(ex, "LS25 Downloads-Liste konnte nicht geladen werden");
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Rows.Clear();
                if (downloaded is not null && installedNames is not null)
                {
                    foreach (var m in downloaded)
                    {
                        var row = new ModRow(m);
                        row.IsAlreadyInstalled = installedNames.Contains(Normalize(m.FileName));
                        Rows.Add(row);
                    }
                    var totalBytes = Rows.Sum(r => r.Source.FileSizeBytes);
                    Summary = Rows.Count == 0
                        ? "Keine heruntergeladenen Mods."
                        : $"{Rows.Count} ZIPs · {totalBytes / 1024.0 / 1024.0:F1} MB gesamt";
                }
                else
                {
                    Summary = $"Fehler beim Lesen des Downloads-Ordners: {error}";
                }
                _ = LoadPreviewsAsync(Rows.ToArray());
            });
        });
    }

    private async Task LoadPreviewsAsync(ModRow[] rows)
    {
        foreach (var row in rows)
        {
            try
            {
                var path = await _previews.GetOrExtractInstalledPreviewAsync(row.Source.FilePath);
                if (path is null || !File.Exists(path)) continue;
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
                    _host.Logger.Warn(ex, "Downloads-Preview-Bitmap-Decode {p}", path);
                    continue;
                }
                await Dispatcher.UIThread.InvokeAsync(() => row.Preview = bmp);
            }
            catch (Exception ex) { _host.Logger.Debug(ex, "Downloads-Preview-Extract {p}", row.Source.FilePath); }
        }
    }

    /// <summary>Filename-Normalisierung für Fuzzy-Compare Downloads ↔ Installiert.
    /// Suffixe .zip/.disabled abschneiden, lowercase, damit sich der Vergleich
    /// robust gegen aktive/inaktive Varianten verhält.</summary>
    private static string Normalize(string fn)
    {
        var s = fn.ToLowerInvariant();
        if (s.EndsWith(".disabled")) s = s.Substring(0, s.Length - ".disabled".Length);
        if (s.EndsWith(".zip")) s = s.Substring(0, s.Length - ".zip".Length);
        return s;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void InstallSelected() => InstallRow(Selected);

    [RelayCommand]
    private void InstallRow(ModRow? row)
    {
        if (row is null) return;
        try
        {
            var installed = _installer.Install(row.Source.FilePath, overwrite: false);
            _host.Notifications.Notify($"Installiert: {installed.FileName}", NotificationLevel.Success);
            _downloadBus.RaiseModInstalled(installed.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25 Install-from-download fehlgeschlagen");
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync() => await DeleteRowAsync(Selected);

    [RelayCommand]
    private async Task DeleteRowAsync(ModRow? row)
    {
        if (row is null) return;
        bool ok = await _host.Dialogs.ConfirmAsync(
            "Download löschen",
            $"„{row.Source.FileName}“ aus dem Downloads-Ordner löschen?",
            okLabel: "Löschen", cancelLabel: "Abbrechen");
        if (!ok) return;
        try
        {
            _installer.DeleteDownload(row.Source.FilePath);
            _host.Notifications.Notify($"Gelöscht: {row.Source.FileName}", NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private void OpenDownloadsFolder() => _host.Shell.OpenDirectory(DownloadsDir);

    /// <summary>Öffnet ModDetailWindow für die Row via Fuzzy-Match auf den
    /// ModHub-Katalog. Braucht einen geladenen Katalog-Cache (User war schon
    /// mal im ModHub-Tab). Ohne Katalog-Cache oder ohne Match → Info-Toast.
    /// Analog InstalledModsViewModel.ShowDetail v1.8.0.</summary>
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

    /// <summary>Fuzzy-Match Filename → CatalogEntry (analog LS-ModManager +
    /// InstalledModsViewModel). Kopie damit DownloadsViewModel unabhängig
    /// bleibt.</summary>
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
        if (result.EndsWith("disabled")) result = result.Substring(0, result.Length - "disabled".Length);
        return result;
    }

    private static int? ExtractModIdFromUrl(string url)
    {
        var m = System.Text.RegularExpressions.Regex.Match(url, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }
}
