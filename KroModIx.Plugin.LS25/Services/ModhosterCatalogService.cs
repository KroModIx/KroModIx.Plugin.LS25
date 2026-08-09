using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using HtmlAgilityPack;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Katalog-Client für <c>modhoster.de</c> (zweite Mod-Quelle neben dem
/// GIANTS ModHub). Nutzt den offiziellen JSON-Endpunkt
/// <c>/mods.json?game_id=1</c> (game_id=1 ist Landwirtschafts Simulator 25).
/// <para>
/// <b>Kein In-App-Download</b> — modhoster verlangt Login-Session für die
/// eigentliche ZIP, und die robots.txt sperrt <c>/external/</c>, <c>/redirect/</c>
/// und <c>/login</c> explizit. Der Nutzer klickt in der App auf „🌐 Öffnen"
/// und macht den Download im Browser.
/// </para>
/// </summary>
public sealed class ModhosterCatalogService : IDisposable
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private const string BaseUrl = "https://www.modhoster.de";
    // game_id=1 ist bei modhoster explizit „Landwirtschafts Simulator 25"
    // (das game_name-Feld im JSON bestätigt es).
    private const int Ls25GameId = 1;

    private readonly HttpClient _http;

    public ModhosterCatalogService(HttpClient http)
    {
        // HttpClient kommt vom Host (proxy-aware via IHostServices.CreateHttpClient).
        _http = http;
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    /// <summary>
    /// Holt die aktuellen „⭐ Staff Picks" von der Modhoster-Startseite —
    /// vier redaktionell gepflegte Empfehlungen, die im JSON-API-Feed nicht
    /// als Flag mitkommen (die API hat kein <c>staff_pick</c>-Feld, nur ein
    /// unbenutztes <c>premium</c>). Rückgabe: Set der vollständigen DetailUrls
    /// (dasselbe Format wie im Katalog-Eintrag), damit der Caller sie 1:1
    /// gegen <see cref="ModHubEntry.DetailUrl"/> matchen kann.
    ///
    /// <para>Best-Effort: bei HTTP-Fehler oder Layout-Änderung wird ein leeres
    /// Set zurückgegeben (Log-Warning), das Featured-Update wird für Modhoster
    /// stumm übersprungen — der reguläre Katalog bleibt komplett funktional.</para>
    /// </summary>
    public async Task<IReadOnlySet<string>> FetchStaffPickSlugsAsync(
        CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/spiel/ls-25";
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            // Startseite liefert HTML statt JSON — Accept-Default ändern.
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");
            using var resp = await _http.SendAsync(req, timeoutCts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(timeoutCts.Token).ConfigureAwait(false);
            return ParseStaffPickSlugs(html);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Modhoster-Staff-Picks konnten nicht geladen werden");
            return new HashSet<string>();
        }
    }

    /// <summary>Testbar: HTML → Set von DetailUrls der Staff-Picks.
    /// Selektor: alle <c>&lt;a class="modcard featured"&gt;</c>-Anker, deren
    /// <c>href</c> mit <c>/mods/</c> beginnt. Die Klasse „featured" markiert
    /// im Modhoster-Layout die Staff-Pick-Cards eindeutig.</summary>
    public static IReadOnlySet<string> ParseStaffPickSlugs(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anchors = doc.DocumentNode.SelectNodes(
            "//a[contains(concat(' ',normalize-space(@class),' '),' featured ')]");
        if (anchors is null) return result;
        foreach (var a in anchors)
        {
            var href = a.GetAttributeValue("href", "");
            if (string.IsNullOrEmpty(href) || !href.StartsWith("/mods/", StringComparison.Ordinal))
                continue;
            // Auf vollständige DetailUrl bringen — matcht dann exakt was
            // FetchCatalogPageAsync in ModHubEntry.DetailUrl schreibt.
            result.Add($"{BaseUrl}{href}");
        }
        return result;
    }

    public async Task<IReadOnlyList<ModHubEntry>> FetchCatalogPageAsync(
        int page, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/mods.json?game_id={Ls25GameId}&page={page}";
        Log.Info("Modhoster-Katalog laden: {url}", url);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));
            var json = await _http.GetStringAsync(url, timeoutCts.Token).ConfigureAwait(false);
            return ParseCatalogJson(json);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Modhoster-Fetch fehlgeschlagen: {url}", url);
            return Array.Empty<ModHubEntry>();
        }
    }

    /// <summary>Testbar: JSON → ModHubEntry-Liste.</summary>
    public static IReadOnlyList<ModHubEntry> ParseCatalogJson(string json)
    {
        var doc = JsonSerializer.Deserialize<ModhosterResponse>(json);
        if (doc?.Modifications is null || doc.Modifications.Count == 0)
            return Array.Empty<ModHubEntry>();

        var result = new List<ModHubEntry>(doc.Modifications.Count);
        foreach (var m in doc.Modifications)
        {
            if (string.IsNullOrWhiteSpace(m.CachedSlug) || string.IsNullOrWhiteSpace(m.Name))
                continue;
            var detailUrl = $"{BaseUrl}/mods/{m.CachedSlug}";
            var previewUrl = m.Image?.Urls?.Shop
                          ?? m.Image?.Urls?.Thumb
                          ?? m.ThumbUrl
                          ?? m.ImageUrl
                          ?? "";
            result.Add(new ModHubEntry(
                Title: m.Name!,
                Author: m.User?.Name ?? "",
                Category: m.GameName ?? "",
                PreviewUrl: previewUrl,
                DetailUrl: detailUrl,
                Version: null,
                SizeText: null,
                Source: ModHubEntry.ModhosterSource,
                CanInAppDownload: false));
        }
        return result;
    }

    public void Dispose() => _http.Dispose();

    // --- JSON-DTOs (nur was wir wirklich brauchen) ---

    private sealed class ModhosterResponse
    {
        [JsonPropertyName("modifications")] public List<Modification>? Modifications { get; set; }
    }

    private sealed class Modification
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("cached_slug")] public string? CachedSlug { get; set; }
        [JsonPropertyName("game_name")] public string? GameName { get; set; }
        [JsonPropertyName("image_url")] public string? ImageUrl { get; set; }
        [JsonPropertyName("thumb_url")] public string? ThumbUrl { get; set; }
        [JsonPropertyName("image")] public ModImage? Image { get; set; }
        [JsonPropertyName("user")] public ModUser? User { get; set; }
    }

    private sealed class ModImage
    {
        [JsonPropertyName("urls")] public ImageUrls? Urls { get; set; }
    }

    private sealed class ImageUrls
    {
        [JsonPropertyName("shop")] public string? Shop { get; set; }
        [JsonPropertyName("thumb")] public string? Thumb { get; set; }
    }

    private sealed class ModUser
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }
}
