using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Persistenter Katalog-Cache. GIANTS liefert keinen search-Parameter, wir
/// müssen clientseitig alle Seiten sammeln (~15 s). Cache überlebt App-
/// Neustarts. Sidecar-Datei enthält bekannte DetailUrls für die "neue Mods
/// seit letztem Start"-Erkennung.
/// </summary>
public sealed class CatalogCache
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    private readonly Ls25Paths _paths;

    public CatalogCache(Ls25Paths paths) => _paths = paths;

    private string CachePath(string language) =>
        Path.Combine(_paths.CatalogCacheDir, $"catalog-{language}.json");

    private string SeenPath(string language) =>
        Path.Combine(_paths.CatalogCacheDir, $"catalog-{language}-seen.txt");

    public HashSet<string>? LoadSeenSnapshot(string language)
    {
        var path = SeenPath(language);
        if (!File.Exists(path)) return null;
        try
        {
            return new HashSet<string>(File.ReadAllLines(path), StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Seen-Snapshot defekt — ignoriere: {Path}", path);
            return null;
        }
    }

    public void SaveSeenSnapshot(IEnumerable<string> detailUrls, string language)
    {
        var path = SeenPath(language);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, detailUrls);
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex) { Log.Warn(ex, "Seen-Snapshot konnte nicht geschrieben werden: {Path}", path); }
    }

    public void Save(IEnumerable<ModHubEntry> entries, string language)
    {
        var path = CachePath(language);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var payload = new CatalogSnapshot(DateTime.UtcNow, language, entries.ToList());
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(payload, JsonOpts));
        File.Move(tmp, path, overwrite: true);
        Log.Info("Katalog-Cache geschrieben: {N} Einträge → {Path}", payload.Entries.Count, path);
    }

    public CatalogSnapshot? Load(string language)
    {
        var path = CachePath(language);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(json);
            if (snapshot is null) return null;
            Log.Info("Katalog-Cache geladen: {N} Einträge (Alter: {Age})",
                snapshot.Entries.Count, DateTime.UtcNow - snapshot.SavedUtc);
            return snapshot;
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Katalog-Cache defekt — ignoriere: {Path}", path);
            return null;
        }
    }
}
