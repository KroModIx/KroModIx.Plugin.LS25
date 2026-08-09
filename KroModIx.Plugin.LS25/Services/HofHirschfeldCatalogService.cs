using System.Net.Http;
using System.Text.RegularExpressions;
using System.Web;
using HtmlAgilityPack;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Dritte Katalog-Quelle: <c>hof-hirschfeld.de</c> (Community-Umbauten,
/// „Hirschfeld-Version" von Standard-Mods, alle für LS25). Kein zentraler
/// Katalog-Endpoint — wir iterieren über die Kategorien-Liste, die von der
/// Startseite gescrapt wird.
/// <para>
/// <b>Kein In-App-Download</b> — die Download-Buttons stecken hinter einem
/// „Werbung erlauben und Download freischalten"-Consent-Overlay (die Seite
/// ist werbefinanziert und die Betreiber machen das explizit klar). Ohne
/// JavaScript/Cookie-Handling ist der eigentliche ZIP-Link nicht erreichbar
/// — und den Consent umgehen wäre unfair gegenüber der Community-Site.
/// Deshalb: Katalog-Anzeige + Browser-Öffnen für Download.
/// </para>
/// </summary>
public sealed class HofHirschfeldCatalogService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string BaseUrl = "https://www.hof-hirschfeld.de";
    private const string Author = "Hof Hirschfeld";

    private readonly HttpClient _http;

    public HofHirschfeldCatalogService(HttpClient http)
    {
        // HttpClient kommt vom Host (proxy-aware via IHostServices.CreateHttpClient).
        _http = http;
    }

    /// <summary>
    /// Holt die Startseite und extrahiert die Kategorie-Slugs. Wenn das fehl-
    /// schlägt, fällt der Full-Load auf die bekannte Kategorien-Liste zurück.
    /// </summary>
    public async Task<IReadOnlyList<string>> FetchCategorySlugsAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            var html = await _http.GetStringAsync(BaseUrl + "/", timeoutCts.Token)
                .ConfigureAwait(false);
            var slugs = ParseCategorySlugs(html);
            return slugs.Count > 0 ? slugs : FallbackCategories;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Hof-Hirschfeld: Kategorien-Load fehlgeschlagen — Fallback-Liste");
            return FallbackCategories;
        }
    }

    /// <summary>Testbar: extrahiert alle <c>/category/&lt;slug&gt;</c> aus HTML.</summary>
    public static IReadOnlyList<string> ParseCategorySlugs(string html)
    {
        var slugs = new HashSet<string>();
        foreach (Match m in Regex.Matches(html, @"href=""[^""]*/category/([^""?]+)"""))
        {
            var slug = m.Groups[1].Value.Trim('/');
            if (!string.IsNullOrWhiteSpace(slug) && !slug.Contains('=')) slugs.Add(slug);
        }
        return slugs.ToList();
    }

    /// <summary>Holt eine Kategorie-Seite und parst die Mod-Karten.</summary>
    public async Task<IReadOnlyList<ModHubEntry>> FetchCategoryPageAsync(
        string categorySlug, int page, CancellationToken ct = default)
    {
        var url = page > 1
            ? $"{BaseUrl}/category/{categorySlug}?page={page}"
            : $"{BaseUrl}/category/{categorySlug}";
        Log.Info("Hof-Hirschfeld-Katalog laden: {url}", url);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var html = await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return ParseCategoryPage(html, categorySlug);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Hof-Hirschfeld-Fetch fehlgeschlagen: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
    }

    /// <summary>
    /// Testbar: HTML einer Kategorie-Seite → ModHubEntry-Liste. Karten haben die
    /// CSS-Struktur <c>a.mod-card__media[href="/mod/…"]</c> mit einem
    /// <c>&lt;img&gt;</c> darin.
    /// </summary>
    public static IReadOnlyList<ModHubEntry> ParseCategoryPage(string html, string categorySlug)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var anchors = doc.DocumentNode.SelectNodes("//a[contains(@class,'mod-card__media')]")
                      ?? new HtmlNodeCollection(doc.DocumentNode);

        var entries = new List<ModHubEntry>();
        var seen = new HashSet<string>();
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            if (!href.Contains("/mod/", StringComparison.Ordinal)) continue;
            var detailUrl = href.StartsWith("http") ? href : BaseUrl + href;
            if (!seen.Add(detailUrl)) continue;

            var img = a.SelectSingleNode(".//img");
            var title = HttpUtility.HtmlDecode(img?.GetAttributeValue("alt", "") ?? "").Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = SlugToTitle(href);
            var previewUrl = img?.GetAttributeValue("src", "") ?? "";
            if (previewUrl.StartsWith("/")) previewUrl = BaseUrl + previewUrl;

            entries.Add(new ModHubEntry(
                Title: title,
                Author: Author,
                Category: CategorySlugToLabel(categorySlug),
                PreviewUrl: previewUrl,
                DetailUrl: detailUrl,
                Version: null,
                SizeText: null,
                Source: ModHubEntry.HofHirschfeldSource,
                CanInAppDownload: false));
        }
        return entries;
    }

    /// <summary>Ermittelt die Gesamtseitenzahl aus der pagination-Nav (default 1).</summary>
    public static int ExtractMaxPage(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var pageLinks = doc.DocumentNode.SelectNodes("//nav[contains(@class,'pagination')]//a");
        if (pageLinks is null) return 1;
        var max = 1;
        foreach (var a in pageLinks)
        {
            var href = a.GetAttributeValue("href", "");
            var m = Regex.Match(href, @"page=(\d+)");
            if (m.Success && int.TryParse(m.Groups[1].Value, out var n) && n > max)
                max = n;
        }
        return max;
    }

    private static string SlugToTitle(string href)
    {
        var slug = href.Substring(href.LastIndexOf('/') + 1);
        return string.Join(" ", slug.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1)));
    }

    private static string CategorySlugToLabel(string slug) =>
        string.Join(" ", slug.Split('-').Select(s => char.ToUpperInvariant(s[0]) + s.Substring(1)));

    public void Dispose() => _http.Dispose();

    // Fallback wenn Startseiten-Parse scheitert — die 30 aktuellen Kategorien.
    private static readonly IReadOnlyList<string> FallbackCategories = new[]
    {
        "fahrzeuge", "traktoren", "kompakttraktoren", "mittelgrosse-traktoren",
        "grosstraktoren", "lkw", "autos-u-a", "lader", "anhaenger",
        "ackerbau", "bodenbearbeitung", "saeen", "ertragssteigerung",
        "ernter", "maehdrescher", "haeckseltechnik", "gruenland", "ballentechnik",
        "sonderkulturen", "wurzelfruechte", "gemuese", "reis", "baumwolle",
        "trauben-oliven", "forstwirtschaft", "sonstiges", "gebaeude-placeable",
        "script-mods", "autodrive",
    };
}
