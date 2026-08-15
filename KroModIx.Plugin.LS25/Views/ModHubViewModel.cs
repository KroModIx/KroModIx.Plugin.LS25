using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.LS25.Services;
using NLog;

namespace KroModIx.Plugin.LS25.Views;

/// <summary>
/// VM für den ModHub-Katalog-Tab. Aggregiert die drei Quellen (GIANTS ModHub,
/// Hof Hirschfeld, modhoster) in einer Liste — wie im standalone LS-ModManager.
/// GIANTS lädt seitenweise mit Direct-Download, die anderen zwei nur Detail-
/// im-Browser wegen Consent-Overlay / Login-Pflicht.
/// </summary>
public sealed partial class ModHubViewModel : ObservableObject
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string Language = "de";

    private readonly ModHubService _hub;
    private readonly HofHirschfeldCatalogService _hof;
    private readonly ModhosterCatalogService _modhoster;
    private readonly CatalogCache _cache;
    private readonly ModInstallService _installer;
    private readonly ModPreviewService _previews;
    private readonly Ls25SettingsService _settings;
    private readonly DownloadEventBus _downloadBus;
    private readonly IHostServices _host;

    private readonly List<ModHubEntry> _allEntries = new();
    private HashSet<string>? _seenSnapshot;
    private CancellationTokenSource? _fullLoadCts;
    // v1.17.0: pro GIANTS-Kategorie-Filter-Key die DetailUrls der Mods
    // in dieser Kategorie. Wird lazy per Server-Roundtrip populiert wenn
    // der User die Kategorie erstmalig auswaehlt (bis zu 5 Seiten Pagination,
    // hard cap). Damit filtert der Client echt statt gegen das Rubrik-Badge.
    private readonly Dictionary<string, HashSet<string>> _categoryUrls =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<HashSet<string>>> _categoryUrlLoads =
        new(StringComparer.OrdinalIgnoreCase);

    public ModHubViewModel(ModHubService hub, HofHirschfeldCatalogService hof,
        ModhosterCatalogService modhoster, CatalogCache cache,
        ModInstallService installer, ModPreviewService previews,
        Ls25SettingsService settings, DownloadEventBus downloadBus,
        IHostServices host)
    {
        _hub = hub;
        _hof = hof;
        _modhoster = modhoster;
        _cache = cache;
        _installer = installer;
        _previews = previews;
        _settings = settings;
        _downloadBus = downloadBus;
        _host = host;

        Categories = new ObservableCollection<ModHubCategory>
        {
            new("", Strings.T("filter.all_categories")),
        };
        SelectedCategory = Categories[0];

        Sources = new ObservableCollection<SourceFilterOption>
        {
            new(null, Strings.T("filter.all_sources")),
            new(ModHubEntry.GiantsSource, Strings.T("source.giants")),
            new(ModHubEntry.HofHirschfeldSource, Strings.T("source.hof_hirschfeld")),
            new(ModHubEntry.ModhosterSource, Strings.T("source.modhoster")),
        };
        SelectedSource = Sources[0];

        SortOptions = new ObservableCollection<CatalogSortOption>
        {
            new("default",  Strings.T("sort.default")),
            new("neu",      Strings.T("sort.new_first")),
            new("name",     Strings.T("sort.name")),
            new("author",   Strings.T("sort.author")),
            new("category", Strings.T("sort.category")),
        };
        SelectedSort = SortOptions[0];

        _ = InitializeAsync();
    }

    public ObservableCollection<CatalogRow> Rows { get; } = new();
    public ObservableCollection<ModHubCategory> Categories { get; }
    public ObservableCollection<SourceFilterOption> Sources { get; }
    public ObservableCollection<CatalogSortOption> SortOptions { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ModHubCategory? _selectedCategory;

    [ObservableProperty]
    private SourceFilterOption? _selectedSource;

    [ObservableProperty]
    private CatalogSortOption? _selectedSort;

    [ObservableProperty]
    private string _status = Strings.T("status.loading_catalog");

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyPropertyChangedFor(nameof(CanDownloadSelected))]
    [NotifyPropertyChangedFor(nameof(SelectionNeedsBrowser))]
    [NotifyPropertyChangedFor(nameof(CanSummarizeSelected))]
    private CatalogRow? _selected;

    public bool HasSelection => Selected is not null;
    public bool CanDownloadSelected => Selected?.Source.CanInAppDownload == true;
    public bool SelectionNeedsBrowser => Selected is not null && !Selected.Source.CanInAppDownload;

    /// <summary>KI-Zusammenfassung braucht die Detail-Beschreibung. Aktuell nur
    /// bei GIANTS-Rows verfügbar — modhoster/Hof Hirschfeld haben keinen HTTP-
    /// abgreifbaren Description-Text (Login-Pflicht bzw. Consent-Overlay).</summary>
    public bool CanSummarizeSelected =>
        Selected?.Source.Source == ModHubEntry.GiantsSource;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _summaryVisible;

    [ObservableProperty]
    private bool _summaryBusy;

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryChanged(ModHubCategory? value)
    {
        // v1.17.0: Bei aktivem Kategorie-Filter erst die Mod-URLs der
        // Kategorie server-side laden (einmal per Kategorie gecached),
        // dann filtert ApplyFilter gegen dieses Set. Ohne den Server-
        // Roundtrip haetten wir kein Wissen welche Mod in welcher GIANTS-
        // Kategorie liegt — der ursprueng client-side Match gegen das
        // Rubrik-Badge lieferte immer 0 Rows.
        if (value is not null && !string.IsNullOrEmpty(value.Filter)
            && !_categoryUrls.ContainsKey(value.Filter))
        {
            _ = EnsureCategoryUrlsAsync(value.Filter);
        }
        ApplyFilter();
    }
    partial void OnSelectedSourceChanged(SourceFilterOption? value) => ApplyFilter();
    partial void OnSelectedSortChanged(CatalogSortOption? value) => ApplyFilter();

    /// <summary>v1.17.0: laedt die DetailUrls aller Mods einer GIANTS-Kategorie
    /// (bis Page 5, sollte fuer alle Kategorien reichen — die meisten haben
    /// deutlich weniger). Ergebnis wird gecached. Bei Erfolg triggert der
    /// Filter neu → gecachte Rows werden entsprechend gefiltert.</summary>
    private async Task EnsureCategoryUrlsAsync(string filterKey)
    {
        if (_categoryUrls.ContainsKey(filterKey)) return;
        if (_categoryUrlLoads.TryGetValue(filterKey, out var existing))
        {
            await existing;
            return;
        }
        var task = LoadCategoryUrlsCoreAsync(filterKey);
        _categoryUrlLoads[filterKey] = task;
        try
        {
            var urls = await task;
            _categoryUrls[filterKey] = urls;
            // Rows neu filtern falls User noch auf dieser Kategorie steht.
            if (SelectedCategory?.Filter == filterKey)
                await Dispatcher.UIThread.InvokeAsync(ApplyFilter);
        }
        finally { _categoryUrlLoads.Remove(filterKey); }
    }

    private async Task<HashSet<string>> LoadCategoryUrlsCoreAsync(string filterKey)
    {
        var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int MaxPages = 5;
        for (int page = 1; page <= MaxPages; page++)
        {
            IReadOnlyList<ModHubEntry> pageEntries;
            try
            {
                pageEntries = await _hub.FetchCatalogPageAsync(page, Language, filter: filterKey);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Kategorie-Fetch Seite {P} fuer {F} fehlgeschlagen", page, filterKey);
                break;
            }
            if (pageEntries.Count == 0) break;
            foreach (var e in pageEntries)
                if (!string.IsNullOrEmpty(e.DetailUrl)) urls.Add(e.DetailUrl);
            if (pageEntries.Count < 20) break;
            await Task.Delay(200);
        }
        Log.Info("Kategorie {F}: {N} Mods geladen", filterKey, urls.Count);
        return urls;
    }

    private async Task InitializeAsync()
    {
        // Cache-Load (2 MB JSON) MUSS off-UI-Thread laufen — sonst freezt der
        // MainWindow-Sidebar beim App-Start, weil der ModHub-Tab wegen der
        // FS25-Auto-Selection sofort instantiiert wird.
        var (snapshot, seen) = await Task.Run(() =>
            (_cache.Load(Language), _cache.LoadSeenSnapshot(Language)));
        _seenSnapshot = seen;
        if (snapshot is not null)
        {
            await AddEntriesBatchedAsync(snapshot.Entries);
            var ageH = (int)(DateTime.UtcNow - snapshot.SavedUtc).TotalHours;
            Status = string.Format(Strings.T("status.cache_summary"), Rows.Count, ageH);

            // Update-Badge auf der FS25-Kachel zurücksetzen: der User hat den
            // Katalog jetzt gesehen. GameUpdateBadgeService fragt beim
            // nächsten Tick den Notifier neu ab und findet dann 0 unseen
            // Einträge → Badge weg. Ohne diesen Aufruf würde der Badge nur
            // beim expliziten Full-Refresh zurückgesetzt (siehe Zeile 242).
            _cache.SaveSeenSnapshot(snapshot.Entries.Select(e => e.DetailUrl), Language);
        }

        _ = LoadCategoriesAsync();

        // Nur refreshen wenn Cache abgelaufen (analog LS-ModManager). User kann
        // manuell via „Katalog neu laden"-Button erzwingen — RefreshCatalog-
        // Command bypasst diesen Check.
        var maxAge = TimeSpan.FromHours(Math.Max(0, _settings.Current.CatalogRefreshHours));
        var cacheStale = snapshot is null
            || snapshot.Entries.Count == 0
            || DateTime.UtcNow - snapshot.SavedUtc > maxAge;
        if (cacheStale)
        {
            Log.Info("Katalog-Cache abgelaufen ({age}h > {max}h) — Full-Load startet",
                snapshot is null ? 0 : (int)(DateTime.UtcNow - snapshot.SavedUtc).TotalHours,
                _settings.Current.CatalogRefreshHours);
            await RefreshCatalogAsync();
        }
        else
        {
            Log.Info("Katalog-Cache ist frisch ({age}h < {max}h) — kein Refresh",
                (int)(DateTime.UtcNow - snapshot!.SavedUtc).TotalHours,
                _settings.Current.CatalogRefreshHours);
        }
    }

    /// <summary>Fügt Rows in Batches à 200 in die ObservableCollection ein und
    /// yieldet zwischen den Batches per <c>await Task.Delay(1)</c>, damit die
    /// UI-Message-Loop dazwischen rendern kann. Ohne Batching blockiert
    /// 7000× Rows.Add die UI mehrere Sekunden — Sidebar bleibt leer bis
    /// fertig.</summary>
    private async Task AddEntriesBatchedAsync(IReadOnlyList<ModHubEntry> entries)
    {
        const int BatchSize = 200;
        var missingCover = new List<CatalogRow>(entries.Count);
        int batchStart = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            _allEntries.Add(e);
            var row = new CatalogRow(e) { IsNew = _seenSnapshot is not null && !_seenSnapshot.Contains(e.DetailUrl) };
            if (RowMatchesFilter(row))
            {
                Rows.Add(row);
                if (!string.IsNullOrWhiteSpace(row.Source.PreviewUrl))
                    missingCover.Add(row);
            }
            if (i - batchStart >= BatchSize)
            {
                batchStart = i;
                Status = string.Format(Strings.T("status.cache_batch"), i, entries.Count);
                await Task.Delay(1);
            }
        }
        if (missingCover.Count > 0) _ = LoadCoversForAsync(missingCover);
    }

    private async Task LoadCategoriesAsync()
    {
        try
        {
            var html = await _hub.FetchCatalogPageHtmlAsync(1, Language);
            if (html is null) return;
            var cats = ModHubService.ParseCategories(html);
            foreach (var cat in cats.Where(c => Categories.All(x => x.Filter != c.Filter)))
                Categories.Add(cat);
        }
        catch (Exception ex) { Log.Debug(ex, "Kategorien-Load fehlgeschlagen"); }
    }

    [RelayCommand]
    private async Task RefreshCatalogAsync()
    {
        _fullLoadCts?.Cancel();
        _fullLoadCts = new CancellationTokenSource();
        var ct = _fullLoadCts.Token;

        IsBusy = true;
        Status = Strings.T("status.catalog_full_load");
        try
        {
            var giantsTask = LoadGiantsAsync(ct);
            var hofTask = LoadHofHirschfeldAsync(ct);
            var modhosterTask = LoadModhosterAsync(ct);
            await Task.WhenAll(giantsTask, hofTask, modhosterTask);

            _cache.Save(_allEntries, Language);
            _cache.SaveSeenSnapshot(_allEntries.Select(e => e.DetailUrl), Language);
            Status = string.Format(Strings.T("status.catalog_total"), _allEntries.Count, Rows.Count);
        }
        catch (OperationCanceledException) { /* silent */ }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-Load abgebrochen");
            Status = string.Format(Strings.T("status.catalog_load_error"), ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadGiantsAsync(CancellationToken ct)
    {
        int page = 1;
        while (!ct.IsCancellationRequested)
        {
            var pageEntries = await _hub.FetchCatalogPageAsync(page, Language, ct);
            if (pageEntries.Count == 0) break;
            AddEntries(pageEntries);
            Status = string.Format(Strings.T("status.giants_page"), page, Rows.Count);
            page++;
            if (pageEntries.Count < 20) break;
            await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
        }
    }

    private async Task LoadHofHirschfeldAsync(CancellationToken ct)
    {
        try
        {
            var slugs = await _hof.FetchCategorySlugsAsync(ct);
            foreach (var slug in slugs)
            {
                if (ct.IsCancellationRequested) return;
                var entries = await _hof.FetchCategoryPageAsync(slug, 1, ct);
                AddEntries(entries);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Hof-Hirschfeld-Load fehlgeschlagen"); }
    }

    private async Task LoadModhosterAsync(CancellationToken ct)
    {
        try
        {
            int page = 1;
            while (!ct.IsCancellationRequested)
            {
                var entries = await _modhoster.FetchCatalogPageAsync(page, ct);
                if (entries.Count == 0) break;
                AddEntries(entries);
                page++;
                if (entries.Count < 20) break;
                if (page > 20) break; // safety
                await Task.Delay(TimeSpan.FromMilliseconds(300), ct);
            }
        }
        catch (Exception ex) { Log.Debug(ex, "Modhoster-Load fehlgeschlagen"); }
    }

    private int AddEntries(IEnumerable<ModHubEntry> entries)
    {
        int added = 0;
        var seen = new HashSet<string>(_allEntries.Select(e => e.DetailUrl), StringComparer.Ordinal);
        var missingCover = new List<CatalogRow>();
        foreach (var entry in entries)
        {
            if (!seen.Add(entry.DetailUrl)) continue;
            _allEntries.Add(entry);
            var row = new CatalogRow(entry) { IsNew = _seenSnapshot is not null && !_seenSnapshot.Contains(entry.DetailUrl) };
            if (RowMatchesFilter(row))
            {
                Rows.Add(row);
                if (!string.IsNullOrWhiteSpace(row.Source.PreviewUrl))
                    missingCover.Add(row);
            }
            added++;
        }
        if (missingCover.Count > 0) _ = LoadCoversForAsync(missingCover);
        return added;
    }

    // Parallelität für Cover-Downloads. 6 gleichzeitige Requests halten das
    // GIANTS-CDN happy und beschleunigen den Erst-Load bei 7000 Katalog-
    // einträgen von ~24 min auf ~4 min.
    private static readonly SemaphoreSlim _coverGate = new(6, 6);

    private async Task LoadCoversForAsync(List<CatalogRow> rows)
    {
        // Batches à 50 mit kurzem Yield dazwischen. Ohne das steht die UI-
        // Message-Queue mit tausenden Dispatcher-Posts voll und die App wirkt
        // einfroren. Innerhalb eines Batches laufen die Downloads parallel
        // (Semaphore).
        const int BatchSize = 50;
        int loaded = 0;
        for (int i = 0; i < rows.Count; i += BatchSize)
        {
            var batch = rows.Skip(i).Take(BatchSize)
                .Where(r => r.Cover is null && !string.IsNullOrWhiteSpace(r.Source.PreviewUrl))
                .Select(LoadOneCoverAsync)
                .ToArray();
            if (batch.Length == 0) continue;
            try { await Task.WhenAll(batch); }
            catch { /* Einzelfehler im Log */ }
            loaded += batch.Length;
            await Task.Delay(20); // UI-Thread Luft geben
        }
        Log.Info("LoadCoversForAsync: {n} Rows verarbeitet", loaded);
    }

    private async Task LoadOneCoverAsync(CatalogRow row)
    {
        await _coverGate.WaitAsync();
        try
        {
            var path = await _previews.GetOrDownloadCoverAsync(row.Source.PreviewUrl);
            if (path is null || !File.Exists(path)) return;
            // Bitmap OFF-UI-Thread dekodieren — Skia auf Linux liest den Stream
            // ohne GL-Kontext. Nur die Property-Zuweisung MUSS auf UI-Thread
            // (weil der Bindings-Push den PropertyChanged-Event feuert).
            Bitmap? bmp = null;
            try
            {
                bmp = await Task.Run(() =>
                {
                    using var s = File.OpenRead(path);
                    return new Bitmap(s);
                });
            }
            catch (Exception ex) { Log.Warn(ex, "Cover-Bitmap-Decode {p}", path); return; }
            await Dispatcher.UIThread.InvokeAsync(() => row.Cover = bmp);
        }
        catch (Exception ex) { Log.Warn(ex, "Cover-Load fehlgeschlagen: {u}", row.Source.PreviewUrl); }
        finally { _coverGate.Release(); }
    }

    private void ApplyFilter()
    {
        Rows.Clear();
        var missingCover = new List<CatalogRow>();

        // v1.17.0: bei aktivem Kategorie-Filter der noch laedt eine kurze
        // Status-Info geben, sonst sieht die 0-Row-View nach „Bug" aus.
        if (SelectedCategory is not null && !string.IsNullOrEmpty(SelectedCategory.Filter)
            && !_categoryUrls.ContainsKey(SelectedCategory.Filter))
        {
            Status = string.Format(Strings.T("status.category_loading"), SelectedCategory.Label);
        }

        // Installed-Titel einmal normalisieren (Fuzzy-Match für ✓ INSTALLIERT-
        // Badge). Analog LS-ModManager: normalisierte Filename gegen
        // normalisierten Titel, Substring in beide Richtungen.
        var installedNorms = _installer.ListInstalled()
            .Select(m => NormalizeForMatch(m.Metadata?.Title ?? Path.GetFileNameWithoutExtension(m.FileName)))
            .Where(n => n.Length >= 3)
            .ToList();

        var candidates = new List<CatalogRow>();
        foreach (var e in _allEntries)
        {
            var row = new CatalogRow(e)
            {
                IsNew = _seenSnapshot is not null && !_seenSnapshot.Contains(e.DetailUrl),
            };
            if (!RowMatchesFilter(row)) continue;
            // Fuzzy: Titel-Normalisierung, dann Substring-Match in beide
            // Richtungen (Katalog-Titel ⊂ Filename oder umgekehrt).
            var titleNorm = NormalizeForMatch(row.Title);
            row.IsInstalled = titleNorm.Length >= 3 &&
                installedNorms.Any(f => f.Contains(titleNorm) || titleNorm.Contains(f));
            candidates.Add(row);
        }

        // Sortieren: SelectedSort.Key entscheidet.
        var sorted = SortCandidates(candidates);
        foreach (var row in sorted)
        {
            Rows.Add(row);
            if (!string.IsNullOrWhiteSpace(row.Source.PreviewUrl))
                missingCover.Add(row);
        }
        if (missingCover.Count > 0) _ = LoadCoversForAsync(missingCover);
    }

    private IEnumerable<CatalogRow> SortCandidates(List<CatalogRow> rows) =>
        (SelectedSort?.Key ?? "default") switch
        {
            "neu"      => rows.OrderByDescending(r => r.IsNew).ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            "name"     => rows.OrderBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            "author"   => rows.OrderBy(r => r.Author, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            "category" => rows.OrderBy(r => r.Category, StringComparer.CurrentCultureIgnoreCase)
                             .ThenBy(r => r.Title, StringComparer.CurrentCultureIgnoreCase),
            _          => rows.AsEnumerable(),
        };

    /// <summary>Normalisierung für Fuzzy-Match Titel ↔ Filename. Nur
    /// Buchstaben/Ziffern, lowercase, FS/LS-Präfixe abschneiden. Analog
    /// InstalledModsViewModel.NormalizeForMatch (bewusste Duplikation —
    /// eine kleine Helper-Klasse wäre Overkill).</summary>
    internal static string NormalizeForMatch(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        var result = sb.ToString();
        foreach (var prefix in new[] { "fs25", "fs22", "ls25", "ls22" })
            if (result.StartsWith(prefix)) result = result.Substring(prefix.Length);
        return result;
    }

    private bool RowMatchesFilter(CatalogRow row)
    {
        if (SelectedSource?.SourceKey is string src && !string.Equals(row.Source.Source, src, StringComparison.Ordinal))
            return false;

        var q = SearchText?.Trim();
        if (!string.IsNullOrEmpty(q))
        {
            if (!(row.Source.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                  || row.Source.Author.Contains(q, StringComparison.OrdinalIgnoreCase)
                  || row.Source.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                return false;
        }
        if (SelectedCategory is not null && !string.IsNullOrEmpty(SelectedCategory.Filter))
        {
            // v1.17.0: echter Filter gegen die vom Server gelieferten Mod-URLs
            // der Kategorie. Solange das Set noch laedt (per EnsureCategory-
            // UrlsAsync) zeigen wir keine Rows — sonst waere die Reihenfolge
            // "alles kurz weg → Set da → 5 Rows plopp" verwirrend.
            if (!_categoryUrls.TryGetValue(SelectedCategory.Filter, out var allowed))
                return false;
            if (string.IsNullOrEmpty(row.Source.DetailUrl)) return false;
            if (!allowed.Contains(row.Source.DetailUrl)) return false;
        }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanDownloadSelected))]
    private async Task DownloadSelectedAsync()
    {
        if (Selected is null || !Selected.Source.CanInAppDownload) return;

        int? modId = ExtractModId(Selected.Source.DetailUrl);
        if (modId is null)
        {
            Log.Warn("Download abgebrochen — kein mod_id aus URL extrahierbar: {url}",
                Selected.Source.DetailUrl);
            _host.Notifications.Notify(
                string.Format(Strings.T("notify.no_mod_id_from_url"), Selected.Source.DetailUrl),
                NotificationLevel.Warning);
            return;
        }
        Log.Info("Starte Download: mod_id={id} · Titel={title}", modId, Selected.Source.Title);

        using var scope = _host.BeginProgress(string.Format(Strings.T("progress.download_prefix"), Selected.Source.Title));
        var progress = new Progress<ModDownloadProgress>(p =>
        {
            var frac = p.Fraction ?? 0;
            scope.Report(frac, p.FormatShort());
        });
        try
        {
            var result = await _hub.DownloadModAsync(modId.Value, Language, progress,
                default, Selected.Source.PreviewUrl);
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
            Log.Warn(ex, "ModHub-Download fehlgeschlagen für {Title} (mod_id={Id})",
                Selected.Source.Title, modId);
            _host.Notifications.Notify(string.Format(Strings.T("notify.error_prefix"), ex.Message), NotificationLevel.Error);
        }
    }

    /// <summary>Direkt-Download für eine Row aus dem Row-Button (nicht per
    /// Selected). Der User klickt auf Herunterladen — die Row wird automatisch
    /// selektiert und der Download läuft. Vermeidet den "erst selektieren,
    /// dann Toolbar-Button klicken"-Zweischritt.</summary>
    [RelayCommand]
    private async Task DownloadFromRowAsync(CatalogRow? row)
    {
        if (row is null || !row.Source.CanInAppDownload) return;
        Selected = row;
        await DownloadSelectedAsync();
    }

    [RelayCommand]
    private void OpenRowInBrowser(CatalogRow? row)
    {
        if (row is null) return;
        _host.Shell.OpenExternalUrl(row.Source.DetailUrl);
    }

    [RelayCommand]
    private void ShowDetailForRow(CatalogRow? row)
    {
        if (row is null) return;
        Selected = row;
        ShowDetail();
    }

    /// <summary>Extrahiert die mod_id aus einer GIANTS-Detail-URL. Robust gegen
    /// URL-Varianten: <c>?mod_id=12345</c> (Standard) und <c>/12345/</c> (Legacy).</summary>
    internal static int? ExtractModId(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var m = System.Text.RegularExpressions.Regex.Match(url, @"mod_id=(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var id)) return id;
        // Legacy-Fallback: /modHub/mod/12345 o.ä.
        m = System.Text.RegularExpressions.Regex.Match(url, @"/(\d{4,})/?(?:\?|$)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out id)) return id;
        return null;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void OpenDetailInBrowser()
    {
        if (Selected is null) return;
        _host.Shell.OpenExternalUrl(Selected.Source.DetailUrl);
    }

    /// <summary>Öffnet den Detail-Dialog für GIANTS-Mods. Für die anderen
    /// Quellen (Hof/modhoster) fällt das auf „Detail im Browser" zurück,
    /// da diese Sites die Detail-Beschreibung nicht per HTTP herausgeben.</summary>
    [RelayCommand(CanExecute = nameof(CanSummarizeSelected))]
    private void ShowDetail()
    {
        if (Selected is null) return;
        var modId = ExtractModId(Selected.Source.DetailUrl);
        if (modId is null)
        {
            _host.Shell.OpenExternalUrl(Selected.Source.DetailUrl);
            return;
        }
        var vm = new ModDetailViewModel(modId.Value, Selected, _hub, _previews, _downloadBus, _host);
        var window = new ModDetailWindow { DataContext = vm };
        var owner = (Avalonia.Application.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is not null) window.Show(owner); else window.Show();
    }

    [RelayCommand(CanExecute = nameof(CanSummarizeSelected))]
    private async Task SummarizeSelectedAsync()
    {
        if (Selected is null) return;
        if (!await _host.Ai.IsAvailableAsync())
        {
            _host.Notifications.Notify(
                Strings.T("notify.ai_unavailable"),
                NotificationLevel.Warning);
            return;
        }

        int? modIdOpt = ExtractModId(Selected.Source.DetailUrl);
        if (modIdOpt is not int modId) return;

        SummaryVisible = true;
        SummaryBusy = true;
        SummaryText = string.Format(Strings.T("status.summary_loading"), Selected.Source.Title);
        try
        {
            var detail = await _hub.FetchModDetailAsync(modId, Language);
            if (detail is null || string.IsNullOrWhiteSpace(detail.DescriptionText))
            {
                SummaryText = Strings.T("status.summary_no_desc");
                return;
            }

            SummaryText = string.Format(Strings.T("status.summary_building"), _host.Ai.ProviderInfo);
            var systemPrompt = Strings.T("ai.prompt.summary_system");
            var userPrompt = $"Titel: {detail.Title}\nAutor: {detail.Author}\n\nBeschreibung:\n{detail.DescriptionText}";
            var answer = await _host.Ai.CompleteAsync(systemPrompt, userPrompt);
            SummaryText = string.IsNullOrWhiteSpace(answer)
                ? Strings.T("status.summary_no_answer")
                : answer;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Summarize fehlgeschlagen für Mod {Id}", modId);
            SummaryText = string.Format(Strings.T("notify.error_prefix"), ex.Message);
        }
        finally
        {
            SummaryBusy = false;
        }
    }

    [RelayCommand]
    private void CloseSummary()
    {
        SummaryVisible = false;
        SummaryText = string.Empty;
    }
}

public sealed partial class CatalogRow : ObservableObject
{
    public CatalogRow(ModHubEntry source) => Source = source;
    public ModHubEntry Source { get; }
    public string Title => Source.Title;
    public string Author => Source.Author;
    public string Category => Source.Category;
    public string? Version => Source.Version;
    public string? SizeText => Source.SizeText;

    public string SourceLabel => Source.Source switch
    {
        ModHubEntry.GiantsSource => "GIANTS",
        ModHubEntry.HofHirschfeldSource => "Hof Hirschfeld",
        ModHubEntry.ModhosterSource => "modhoster",
        _ => Source.Source,
    };

    public bool CanInAppDownload => Source.CanInAppDownload;
    public bool NeedsBrowser => !Source.CanInAppDownload;
    public bool IsFeatured => Source.IsFeatured;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BadgeText))]
    private bool _isNew;

    [ObservableProperty]
    private Bitmap? _cover;

    /// <summary>Katalog-Eintrag ist bereits im Mods-Ordner installiert
    /// (Fuzzy-Match nach LS-ModManager-Muster: normalisierter Titel ↔
    /// normalisierter Filename der installierten Mods).</summary>
    [ObservableProperty]
    private bool _isInstalled;

    public string BadgeText => IsNew ? "NEU" : "";
}

public sealed record SourceFilterOption(string? SourceKey, string Label);

/// <summary>Sortier-Modi für die ModHub-Liste. „Standard" ist Katalog-
/// Ladereihenfolge (Featured/NEU zuerst durch die GIANTS-Sortierung).
/// „Neu zuerst" bringt die <see cref="CatalogRow.IsNew"/>-Rows nach oben.</summary>
public sealed record CatalogSortOption(string Key, string Label);
