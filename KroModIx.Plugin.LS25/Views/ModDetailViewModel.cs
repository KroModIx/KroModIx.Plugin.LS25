using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.LS25.Services;
using NLog;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// VM für den Detail-Dialog eines GIANTS-Mods. Lädt die Detail-Seite (Screenshots,
/// vollständige Beschreibung, Metadaten) beim Öffnen im Hintergrund, bietet
/// KI-Zusammenfassung und Download aus dem Dialog heraus. Analog zum
/// standalone LS-ModManager ModDetailViewModel, aber ohne ähnliche-Mods-Empfehlung
/// (die kommt in v0.8).
/// </summary>
public sealed partial class ModDetailViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string Language = "de";

    private readonly ModHubService _hub;
    private readonly ModPreviewService _previews;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;
    private readonly int _modId;
    private readonly string _fallbackTitle;
    private readonly string _fallbackAuthor;
    private readonly string _fallbackCategory;
    private readonly string _fallbackDetailUrl;
    private readonly string _fallbackPreviewUrl;

    public ModDetailViewModel(int modId, CatalogRow row,
        ModHubService hub, ModPreviewService previews,
        DownloadEventBus downloadBus, IHostServices host)
    {
        _modId = modId;
        _hub = hub;
        _previews = previews;
        _downloadBus = downloadBus;
        _host = host;

        _fallbackTitle = row.Source.Title;
        _fallbackAuthor = row.Source.Author;
        _fallbackCategory = row.Source.Category;
        _fallbackDetailUrl = row.Source.DetailUrl;
        _fallbackPreviewUrl = row.Source.PreviewUrl;

        Title = _fallbackTitle;
        Author = _fallbackAuthor;
        Category = _fallbackCategory;
        Description = "Detail-Seite wird geladen …";

        _ = LoadDetailAsync();
    }

    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private string _sizeText = "";
    [ObservableProperty] private string _releaseDate = "";
    [ObservableProperty] private string _platform = "";
    [ObservableProperty] private string _rating = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private string _statusText = "Detail-Seite wird geladen …";
    [ObservableProperty] private bool _isLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSummary))]
    private string _summaryText = "";
    public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);

    [ObservableProperty] private bool _summaryBusy;

    public ObservableCollection<ScreenshotItem> Screenshots { get; } = new();

    private async Task LoadDetailAsync()
    {
        try
        {
            var detail = await _hub.FetchModDetailAsync(_modId, Language);
            if (detail is null)
            {
                Description = "Detail-Seite konnte nicht geladen werden.";
                StatusText = "Fehler beim Laden.";
                return;
            }
            Title = string.IsNullOrWhiteSpace(detail.Title) ? _fallbackTitle : detail.Title;
            Author = string.IsNullOrWhiteSpace(detail.Author) ? _fallbackAuthor : detail.Author;
            Category = string.IsNullOrWhiteSpace(detail.Category) ? _fallbackCategory : detail.Category;
            Version = detail.Version ?? "";
            SizeText = detail.SizeText ?? "";
            ReleaseDate = detail.ReleaseDate ?? "";
            Platform = detail.Platform ?? "";
            Rating = detail.RatingText ?? "";
            Description = string.IsNullOrWhiteSpace(detail.DescriptionText)
                ? "Keine Beschreibung im Detail-Endpoint."
                : detail.DescriptionText;

            foreach (var url in detail.ScreenshotUrls)
                Screenshots.Add(new ScreenshotItem(url));
            _ = LoadScreenshotBitmapsAsync();

            StatusText = $"{Screenshots.Count} Screenshot(s) · v{Version}";
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Detail-Load fehlgeschlagen für mod_id={Id}", _modId);
            Description = $"Fehler: {ex.Message}";
            StatusText = "Fehler beim Laden.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadScreenshotBitmapsAsync()
    {
        foreach (var s in Screenshots)
        {
            try
            {
                var path = await _previews.GetOrDownloadCoverAsync(s.Url);
                if (path is null || !File.Exists(path)) continue;
                Bitmap? bmp = null;
                try
                {
                    bmp = await Task.Run(() =>
                    {
                        using var fs = File.OpenRead(path);
                        return new Bitmap(fs);
                    });
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Screenshot-Bitmap-Decode {p}", path);
                    continue;
                }
                await Dispatcher.UIThread.InvokeAsync(() => s.Bitmap = bmp);
            }
            catch (Exception ex) { Log.Debug(ex, "Screenshot-Download {u}", s.Url); }
        }
    }

    [RelayCommand]
    private void OpenInBrowser() => _host.Shell.OpenExternalUrl(_fallbackDetailUrl);

    [RelayCommand]
    private async Task DownloadAsync()
    {
        using var scope = _host.BeginProgress($"Download: {Title}");
        var progress = new Progress<ModDownloadProgress>(p =>
            scope.Report(p.Fraction ?? 0, p.FormatShort()));
        try
        {
            var result = await _hub.DownloadModAsync(_modId, Language, progress, default, _fallbackPreviewUrl);
            if (result is null)
            {
                _host.Notifications.Notify("Download fehlgeschlagen (siehe Log).", NotificationLevel.Error);
                return;
            }
            _host.Notifications.Notify($"Heruntergeladen: {result.FileName}", NotificationLevel.Success);
            _downloadBus.RaiseDownloadsChanged(result.FileName);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Download aus Detail-Dialog fehlgeschlagen für mod_id={Id}", _modId);
            _host.Notifications.Notify($"Fehler: {ex.Message}", NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (string.IsNullOrWhiteSpace(Description) || IsLoading)
        {
            _host.Notifications.Notify("Bitte warten bis Detail geladen ist.", NotificationLevel.Info);
            return;
        }
        if (!await _host.Ai.IsAvailableAsync())
        {
            _host.Notifications.Notify(
                "KI-Provider nicht erreichbar — bitte in den KroModIx-Einstellungen konfigurieren.",
                NotificationLevel.Warning);
            return;
        }
        SummaryBusy = true;
        SummaryText = $"KI-Zusammenfassung via {_host.Ai.ProviderInfo} …";
        try
        {
            var systemPrompt = "Du bist ein deutschsprachiger LS25-Mod-Reviewer. " +
                "Fasse die Mod-Beschreibung in 3–5 Sätzen zusammen: " +
                "Was macht der Mod? Welche Fahrzeuge/Objekte/Features? Zielgruppe? " +
                "Kein Werbe-Sprech, sachlich.";
            var userPrompt = $"Titel: {Title}\nAutor: {Author}\n\nBeschreibung:\n{Description}";
            var answer = await _host.Ai.CompleteAsync(systemPrompt, userPrompt);
            SummaryText = string.IsNullOrWhiteSpace(answer) ? "KI hat keine Antwort geliefert." : answer;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Summarize im Detail fehlgeschlagen");
            SummaryText = $"Fehler: {ex.Message}";
        }
        finally
        {
            SummaryBusy = false;
        }
    }
}

public sealed partial class ScreenshotItem : ObservableObject
{
    public ScreenshotItem(string url) => Url = url;
    public string Url { get; }

    [ObservableProperty]
    private Bitmap? _bitmap;
}
