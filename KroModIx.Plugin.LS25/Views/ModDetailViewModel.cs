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
        Description = Strings.T("detail.status.loading");

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
    [ObservableProperty] private string _statusText = Strings.T("detail.status.loading");
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
                Description = Strings.T("detail.status.load_error");
                StatusText = Strings.T("detail.status.short_error");
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
                ? Strings.T("detail.no_description")
                : detail.DescriptionText;

            foreach (var url in detail.ScreenshotUrls)
                Screenshots.Add(new ScreenshotItem(url));
            _ = LoadScreenshotBitmapsAsync();

            StatusText = string.Format(Strings.T("detail.status.summary"), Screenshots.Count, Version);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Detail-Load fehlgeschlagen für mod_id={Id}", _modId);
            Description = string.Format(Strings.T("detail.error_prefix"), ex.Message);
            StatusText = Strings.T("detail.status.short_error");
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
                // v1.17.0: Rohbytes ueber ModPreviewService, Decode via
                // Host-Baukasten (IImageDecoder).
                var bytes = await _previews.GetCoverBytesAsync(s.Url);
                if (bytes is null) continue;
                var bmp = await _host.Images.DecodeAsync(bytes);
                if (bmp is null)
                {
                    Log.Debug("Screenshot-Decode fehlgeschlagen: {u}", s.Url);
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
        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.download_prefix"), Title));
        var progress = new Progress<ModDownloadProgress>(p =>
            scope.Report(p.Fraction ?? 0, p.FormatShort()));
        try
        {
            var result = await _hub.DownloadModAsync(_modId, Language, progress, default, _fallbackPreviewUrl);
            if (result is null)
            {
                _host.Notifications.Notify(Strings.T("notify.download_failed"), NotificationLevel.Error);
                return;
            }
            _host.Notifications.Notify(string.Format(Strings.T("notify.downloaded_prefix"), result.FileName), NotificationLevel.Success);
            _downloadBus.RaiseDownloadsChanged(result.FileName);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Download aus Detail-Dialog fehlgeschlagen für mod_id={Id}", _modId);
            _host.Notifications.Notify(string.Format(Strings.T("notify.error_prefix"), ex.Message), NotificationLevel.Error);
        }
    }

    [RelayCommand]
    private async Task SummarizeAsync()
    {
        if (string.IsNullOrWhiteSpace(Description) || IsLoading)
        {
            _host.Notifications.Notify(Strings.T("notify.detail_wait"), NotificationLevel.Info);
            return;
        }
        if (!await _host.Ai.IsAvailableAsync())
        {
            _host.Notifications.Notify(
                Strings.T("notify.ai_unavailable"),
                NotificationLevel.Warning);
            return;
        }
        SummaryBusy = true;
        SummaryText = string.Format(Strings.T("detail.summary.busy"), _host.Ai.ProviderInfo);
        try
        {
            var systemPrompt = Strings.T("ai.prompt.summary_system");
            var userPrompt = $"Titel: {Title}\nAutor: {Author}\n\nBeschreibung:\n{Description}";
            var answer = await _host.Ai.CompleteAsync(systemPrompt, userPrompt);
            SummaryText = string.IsNullOrWhiteSpace(answer) ? Strings.T("detail.summary.no_answer") : answer;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Summarize im Detail fehlgeschlagen");
            SummaryText = string.Format(Strings.T("detail.error_prefix"), ex.Message);
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
