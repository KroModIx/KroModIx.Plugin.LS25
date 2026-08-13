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
        DownloadsDir = installer.DownloadsDir ?? Strings.T("status.downloads_dir_missing");
        RefreshCommand.Execute(null);

        // Auto-Refresh: sobald der ModHub-Tab (oder ein anderer Tab) einen
        // Download in den Downloads-Ordner geschrieben hat, aktualisiert
        // sich diese Liste automatisch — ohne User-Klick auf Refresh.
        _downloadBus.DownloadsChanged += (_, fileName) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Refresh();
                _host.Notifications.Notify(string.Format(Strings.T("notify.downloads_updated"), fileName),
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
        Summary = Strings.T("status.reading_downloads");
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
                        ? Strings.T("status.no_downloads")
                        : string.Format(Strings.T("status.downloads_summary"), Rows.Count, totalBytes / 1024.0 / 1024.0);
                }
                else
                {
                    Summary = string.Format(Strings.T("status.downloads_read_error"), error);
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
            // overwrite=true damit Updates funktionieren (gleicher Filename wird
            // ohne Frage überschrieben — analog Bulk-Install-Verhalten).
            var installed = _installer.Install(row.Source.FilePath, overwrite: true);
            _host.Notifications.Notify(string.Format(Strings.T("notify.installed_prefix"), installed.FileName), NotificationLevel.Success);
            _downloadBus.RaiseModInstalled(installed.FileName);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Logger.Warn(ex, "LS25 Install-from-download fehlgeschlagen");
            _host.Notifications.Notify(string.Format(Strings.T("notify.error_prefix"), ex.Message), NotificationLevel.Error);
        }
    }

    /// <summary>Bulk-Install aller Downloads. Skill Kernprinzip 6a — nach
    /// einem Update-Batch will der User nicht 10× klicken. Fehler pro Row
    /// werden geloggt, der Loop läuft trotzdem weiter (single-broken-Download
    /// blockt nicht den Batch).</summary>
    [RelayCommand]
    private void InstallAll()
    {
        var rows = Rows.ToArray();
        if (rows.Length == 0)
        {
            _host.Notifications.Notify(Strings.T("notify.no_downloads_install"), NotificationLevel.Info);
            return;
        }
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.install_downloads"), rows.Length));
        int done = 0, failed = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            scope.Report((double)i / rows.Length, string.Format(Strings.T("progress.install_row"), i + 1, rows.Length, row.Title));
            try
            {
                var installed = _installer.Install(row.Source.FilePath, overwrite: true);
                _downloadBus.RaiseModInstalled(installed.FileName);
                done++;
            }
            catch (Exception ex)
            {
                _host.Logger.Warn(ex, "LS25 Bulk-Install fehlgeschlagen für {File}", row.FileName);
                failed++;
            }
        }
        var msg = failed == 0
            ? string.Format(Strings.T("notify.downloads_installed"), done)
            : string.Format(Strings.T("notify.downloads_install_partial"), done, failed);
        _host.Notifications.Notify(msg,
            failed == 0 ? NotificationLevel.Success : NotificationLevel.Warning);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync() => await DeleteRowAsync(Selected);

    [RelayCommand]
    private async Task DeleteRowAsync(ModRow? row)
    {
        if (row is null) return;
        bool ok = await _host.Dialogs.ConfirmAsync(
            Strings.T("dialog.delete_download_title"),
            string.Format(Strings.T("dialog.delete_download_msg"), row.Source.FileName),
            okLabel: Strings.T("dialog.btn_delete"), cancelLabel: Strings.T("dialog.btn_cancel"));
        if (!ok) return;
        try
        {
            _installer.DeleteDownload(row.Source.FilePath);
            _host.Notifications.Notify(string.Format(Strings.T("notify.deleted_prefix"), row.Source.FileName), NotificationLevel.Success);
            Refresh();
        }
        catch (Exception ex)
        {
            _host.Notifications.Notify(string.Format(Strings.T("notify.error_prefix"), ex.Message), NotificationLevel.Error);
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
                Strings.T("notify.no_catalog_cache"),
                NotificationLevel.Warning);
            return;
        }
        var entry = LookupCatalogEntry(snapshot.Entries, row.FileName);
        if (entry is null)
        {
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_catalog_match"), row.Title),
                NotificationLevel.Info);
            return;
        }
        var modId = ExtractModIdFromUrl(entry.DetailUrl);
        if (modId is null)
        {
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_mod_id"), entry.DetailUrl),
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
