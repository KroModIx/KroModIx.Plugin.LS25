using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
/// basierte Commands (InstallRow, DeleteRow) für Klick auf Kachel-Button
/// ohne vorherige Selection. IsInstalled-Flag per Filename-Fuzzy-Match gegen
/// die installierten Mods → grünes „✓ INSTALLIERT"-Badge.
/// </summary>
public sealed partial class DownloadsViewModel : ObservableObject
{
    private readonly ModInstallService _installer;
    private readonly ModPreviewService _previews;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;

    public DownloadsViewModel(ModInstallService installer, ModPreviewService previews,
        DownloadEventBus downloadBus, IHostServices host)
    {
        _installer = installer;
        _previews = previews;
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
}
