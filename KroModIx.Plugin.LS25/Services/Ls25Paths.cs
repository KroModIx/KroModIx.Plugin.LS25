using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Plugin-lokale Datei-Pfade — Ersatz für die globale <c>AppPaths</c>-Klasse aus
/// LS-ModManager. Basiert auf <see cref="IHostServices.PluginCacheDir"/> +
/// <see cref="IHostServices.PluginDataDir"/>.
/// </summary>
public sealed class Ls25Paths
{
    private readonly IHostServices _host;

    public Ls25Paths(IHostServices host) => _host = host;

    /// <summary>Persistenter Ordner für Plugin-Konfiguration (settings.json etc.).
    /// Direct-Passthrough des Host-Werts — im Gegensatz zu Cache-Ordnern
    /// bleibt dieser bei App-Uninstall NICHT weg (User-Config).</summary>
    public string PluginDataDir => _host.PluginDataDir;

    /// <summary>Backup-Ordner für Mod-Konfigurations-Snapshots. Liegt bewusst
    /// im PluginDataDir (persistent), nicht im Cache — Backups sollen einen
    /// Cache-Wipe überleben. User kann von hier auf Nextcloud/USB-Stick
    /// kopieren; ein Save-File-Picker kommt in v0.5.1 mit einem
    /// IDialogService.PickSaveFileAsync-Contract-Bump.</summary>
    public string BackupsDir
    {
        get
        {
            var dir = Path.Combine(_host.PluginDataDir, "backups");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Persistenter Ordner für heruntergeladene, noch nicht installierte
    /// Mod-ZIPs. In User-Cache, damit Deinstallation der App ihn nicht sofort
    /// wegräumt.</summary>
    public string DownloadsDir
    {
        get
        {
            var dir = Path.Combine(_host.PluginCacheDir, "downloads");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Cache für Preview-Bilder aus ZIPs und ModHub-Coverbildern.</summary>
    public string PreviewsCacheDir
    {
        get
        {
            var dir = Path.Combine(_host.PluginCacheDir, "previews");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Katalog-Cache-Basisverzeichnis (per-Sprache-Files landen direkt hier).</summary>
    public string CatalogCacheDir
    {
        get
        {
            var dir = Path.Combine(_host.PluginCacheDir, "catalog");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Basis-Cache-Pfad für die Preview eines Mods (ohne Extension).</summary>
    public string PreviewCacheBasePathFor(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        return Path.Combine(PreviewsCacheDir, name);
    }

    /// <summary>Findet eine existierende Preview-Cache-Datei zu einem Mod, egal welche
    /// Bild-Extension sie hat. Nötig weil Avalonia/Skia auf Linux JPG mit .png-Endung
    /// NICHT laden — wir speichern mit echter Extension und suchen beim Read beide.</summary>
    public string? FindExistingPreview(string zipPath)
    {
        var basePath = PreviewCacheBasePathFor(zipPath);
        foreach (var ext in new[] { ".jpg", ".jpeg", ".png" })
        {
            var p = basePath + ext;
            if (File.Exists(p)) return p;
        }
        return null;
    }

    /// <summary>True wenn für den Mod bereits ein Katalog-Cover (nicht ZIP-icon.png)
    /// im Cache liegt. JPG/JPEG → immer Katalog-Cover. PNG → braucht Sidecar-Marker
    /// <c>&lt;basename&gt;.catalog</c>, weil dieselbe Extension auch von ZIP-icon.png
    /// stammen kann.</summary>
    public bool HasCatalogCoverCache(string zipPath)
    {
        var basePath = PreviewCacheBasePathFor(zipPath);
        if (File.Exists(basePath + ".jpg") || File.Exists(basePath + ".jpeg")) return true;
        return File.Exists(basePath + ".png") && File.Exists(basePath + ".catalog");
    }

    public void WriteCatalogCoverMarker(string zipPath)
    {
        var marker = PreviewCacheBasePathFor(zipPath) + ".catalog";
        try { File.WriteAllBytes(marker, System.Array.Empty<byte>()); }
        catch { /* best-effort */ }
    }

    /// <summary>Extension aus den ersten Bytes der Bild-Daten raten. Unterstützt
    /// JPG, PNG und WebP — letzteres liefert modhoster durchgängig.</summary>
    public static string GuessImageExtension(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return ".jpg";
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 &&
            bytes[2] == 0x4E && bytes[3] == 0x47)
            return ".png";
        // WebP: „RIFF????WEBP" — 4 Bytes RIFF, 4 Bytes Size, dann WEBP-Marker.
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return ".webp";
        return ".bin";
    }
}
