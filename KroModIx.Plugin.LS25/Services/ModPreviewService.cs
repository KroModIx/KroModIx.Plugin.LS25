using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Extrahiert und cached Preview-Bilder — für Installiert-Tab aus dem
/// Mod-ZIP (via <see cref="ModDescReader"/>), für ModHub-Tab per HTTP
/// vom GIANTS-CDN. Cache liegt in <see cref="Ls25Paths.PreviewsCacheDir"/>,
/// pro Mod ein basename+Extension. Existierende Cache-Files werden nicht
/// überschrieben — teure Extraktion läuft nur einmal.
/// </summary>
public sealed class ModPreviewService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient DefaultHttp = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Ls25Paths _paths;
    private readonly ModDescReader _reader;
    private readonly HttpClient _http;

    public ModPreviewService(Ls25Paths paths, ModDescReader reader, HttpClient? http = null)
    {
        _paths = paths;
        _reader = reader;
        _http = http ?? DefaultHttp;

        // GIANTS-CDN gibt Cover nur mit Referer frei — sonst HTTP 403.
        // Der Host liefert einen HttpClient mit User-Agent, aber ohne
        // Referer/Accept-Language. Ergänzen (LS-ModManager-Werte).
        if (_http.DefaultRequestHeaders.Referrer is null)
            _http.DefaultRequestHeaders.Referrer = new Uri("https://www.farming-simulator.com/");
        if (_http.DefaultRequestHeaders.AcceptLanguage.Count == 0)
            _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("de-DE,de;q=0.9,en;q=0.5");
    }

    /// <summary>Liefert Cache-Path zu einer Mod-ZIP. Wenn kein Cache existiert,
    /// wird das Preview aus dem ZIP extrahiert und gespeichert. Läuft im Hintergrund-
    /// Thread — extrahieren + DDS-decode kosten je nach Größe 10–100 ms.</summary>
    public async Task<string?> GetOrExtractInstalledPreviewAsync(string zipPath, CancellationToken ct = default)
    {
        var cached = _paths.FindExistingPreview(zipPath);
        if (cached is not null) return cached;

        return await Task.Run(() =>
        {
            try
            {
                var result = _reader.Read(zipPath);
                if (result.PreviewPngBytes is null || result.PreviewPngBytes.Length == 0)
                    return null;
                var ext = Ls25Paths.GuessImageExtension(result.PreviewPngBytes);
                if (ext == ".bin") ext = ".png"; // Reader liefert PNG-Bytes bei DDS-Decode
                var target = _paths.PreviewCacheBasePathFor(zipPath) + ext;
                File.WriteAllBytes(target, result.PreviewPngBytes);
                return target;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Preview-Extract fehlgeschlagen für {p}", zipPath);
                return (string?)null;
            }
        }, ct).ConfigureAwait(false);
    }

    /// <summary>v1.17.0: Bytes-Variante fuer den zentralen Host-Bild-Decoder
    /// (<see cref="IImageDecoder"/>). Nutzt intern
    /// <see cref="GetOrExtractInstalledPreviewAsync"/> und liest das Cache-File
    /// als Bytes zurueck — der Aufrufer schickt die Bytes an
    /// <c>host.Images.DecodeAsync</c> und bekommt eine Avalonia-Bitmap ohne
    /// selbst einen Bitmap-Ctor zu instanziieren.</summary>
    public async Task<byte[]?> GetPreviewBytesAsync(string zipPath, CancellationToken ct = default)
    {
        var path = await GetOrExtractInstalledPreviewAsync(zipPath, ct).ConfigureAwait(false);
        if (path is null) return null;
        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Preview-Bytes-Read fehlgeschlagen für {p}", path);
            return null;
        }
    }

    /// <summary>Ableitung eines stabilen Cache-Keys aus einer Cover-URL. Für
    /// GIANTS-URLs (Format <c>.../storage/&lt;id&gt;/&lt;file&gt;</c>) nehmen wir
    /// die mod_id + Dateiname — der bleibt stabil auch wenn GIANTS die CDN-
    /// Subdomain rotiert (cdn31 → cdn32). Für andere URLs SHA1-Fallback.
    /// v0.7.3 hatte SHA1(volle-URL) verwendet — dadurch wurden nach jeder
    /// CDN-Rotation alle Cover neu heruntergeladen und der Cache wuchs
    /// unnötig, ohne beim UI je zu greifen.</summary>
    internal static string CacheKeyFor(string url)
    {
        var m = Regex.Match(url, @"/storage/(\d+)/([^/?#]+)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var id = m.Groups[1].Value;
            var file = Path.GetFileNameWithoutExtension(m.Groups[2].Value);
            return $"mod{id}_{file}";
        }
        return "sha1_" + Sha1Hex(url);
    }

    /// <summary>Synchrone Cache-Prüfung. Liefert Pfad wenn ein Cover schon
    /// gecacht ist, sonst null — kein Download. Wichtig damit UI-Rows den
    /// Bitmap sofort im gleichen Frame anzeigen können statt einen async
    /// Cover-Load abzuwarten, der sich mit ApplyFilter/Rows.Clear beisst.</summary>
    public string? TryGetCachedCoverPath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var basePath = Path.Combine(_paths.PreviewsCacheDir, "catalog_" + CacheKeyFor(url));
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var candidate = basePath + ext;
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Cover-Download vom ModHub-CDN. Cache-Key basiert auf mod_id
    /// (stabil gegen CDN-Rotation). Dateiendung wird aus den Magic-Bytes der
    /// Response bestimmt (JPG vs PNG).</summary>
    public async Task<string?> GetOrDownloadCoverAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        var basePath = Path.Combine(_paths.PreviewsCacheDir, "catalog_" + CacheKeyFor(url));
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".webp" })
        {
            var candidate = basePath + ext;
            if (File.Exists(candidate)) return candidate;
        }

        try
        {
            Log.Info("Cover-Download start: {url}", url);
            using var res = await _http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode)
            {
                Log.Warn("Cover-Download HTTP {status} für {url}", (int)res.StatusCode, url);
                return null;
            }
            var bytes = await res.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0)
            {
                Log.Warn("Cover-Download leer: {url}", url);
                return null;
            }
            var ext = Ls25Paths.GuessImageExtension(bytes);
            if (ext == ".bin")
            {
                Log.Warn("Cover-Download kein Bild-Magic-Byte ({bytes} B): {url}", bytes.Length, url);
                return null;
            }
            var target = basePath + ext;
            await File.WriteAllBytesAsync(target, bytes, ct);
            Log.Info("Cover gespeichert ({bytes} B) → {target}", bytes.Length, target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Cover-Download-Exception: {url}", url);
            return null;
        }
    }

    /// <summary>v1.17.0: Bytes-Variante fuer den zentralen Host-Bild-Decoder.
    /// Nutzt intern <see cref="GetOrDownloadCoverAsync"/> und liest das
    /// Cache-File als Bytes zurueck. Cover werden auch bei WebP/AVIF/DDS
    /// vom Host korrekt dekodiert — Plugin muss keine Format-Fallbacks
    /// mehr selbst kennen.</summary>
    public async Task<byte[]?> GetCoverBytesAsync(string url, CancellationToken ct = default)
    {
        var path = await GetOrDownloadCoverAsync(url, ct).ConfigureAwait(false);
        if (path is null) return null;
        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Cover-Bytes-Read fehlgeschlagen für {p}", path);
            return null;
        }
    }

    private static string Sha1Hex(string s)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(s));
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
