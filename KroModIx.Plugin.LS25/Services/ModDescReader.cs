using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Liest <c>modDesc.xml</c> aus einer LS/FS-Mod-ZIP und extrahiert die Metadaten
/// plus optional ein Vorschau-PNG. LS25-Mods verwenden meist <c>icon.dds</c> —
/// wir suchen zuerst nach PNG-Alternativen (icon.png, store_*.png), und wenn
/// es keine gibt, dekodieren wir die DDS via <see cref="DdsToPngConverter"/>
/// zu PNG. So bekommen praktisch alle LS25-Mods eine echte Preview statt
/// nur den 🚜-Emoji-Fallback.
///
/// <para>Cache: die Ergebnisse werden pro (Path, Mtime, Size) in einem
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> mit <see cref="Lazy{T}"/>
/// gecacht. Grund: beim App-Start rufen <c>InstalledModsViewModel</c>,
/// <c>DownloadsViewModel</c> und <c>ModHubViewModel.ApplyFilter</c> jeweils
/// <c>ListInstalled()</c> auf — das würde sonst dieselbe ZIP dreimal öffnen
/// (bei 60 Mods = 180 ZIP-Reads + DDS-Decodes). Mit Cache: 60 Reads total,
/// die anderen zwei Aufrufe treffen instant. Lazy&lt;T&gt; verhindert
/// Doppelt-Reads wenn alle 3 VMs den Refresh parallel starten.</para>
/// </summary>
public sealed class ModDescReader
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly string[] LanguagePreference = ["de", "en"];

    private readonly ConcurrentDictionary<string, Lazy<CacheEntry>> _cache = new(StringComparer.OrdinalIgnoreCase);

    private sealed record CacheEntry(long MtimeTicks, long Size, ModReadResult Result);

    public ModReadResult Read(string zipPath)
    {
        FileInfo info;
        try { info = new FileInfo(zipPath); }
        catch (Exception ex)
        {
            Log.Warn(ex, "FileInfo fehlgeschlagen: {Path}", zipPath);
            return new ModReadResult(null, null, ex.Message);
        }
        if (!info.Exists)
            return new ModReadResult(null, null, "Datei nicht gefunden");

        var mtime = info.LastWriteTimeUtc.Ticks;
        var size = info.Length;

        // GetOrAdd + Lazy: bei paralleler Anfrage auf denselben Path führt nur
        // der erste Thread den ZIP-Read aus; alle anderen warten auf Lazy.Value
        // und bekommen dann das gleiche Ergebnis. Kein Locking nötig.
        var lazy = _cache.GetOrAdd(zipPath, _ => new Lazy<CacheEntry>(
            () => new CacheEntry(mtime, size, ReadFromDisk(zipPath)),
            LazyThreadSafetyMode.ExecutionAndPublication));

        var entry = lazy.Value;
        if (entry.MtimeTicks == mtime && entry.Size == size)
            return entry.Result;

        // Datei wurde seit dem Cache-Insert geändert (User hat re-installiert
        // oder der Downloads-Ordner hat eine neuere Version). Fresh-Read.
        var fresh = new Lazy<CacheEntry>(
            () => new CacheEntry(mtime, size, ReadFromDisk(zipPath)),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _cache[zipPath] = fresh;
        return fresh.Value.Result;
    }

    /// <summary>Entfernt eine Datei aus dem Cache — vom <c>ModInstallService</c>
    /// bei Uninstall/Rename aufgerufen, damit stale Einträge nicht ewig im
    /// Speicher bleiben.</summary>
    public void InvalidateCache(string zipPath) => _cache.TryRemove(zipPath, out _);

    private static ModReadResult ReadFromDisk(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            var descEntry = archive.GetEntry("modDesc.xml");
            if (descEntry is null)
                return new ModReadResult(null, null, "modDesc.xml nicht gefunden");

            XDocument doc;
            using (var stream = descEntry.Open())
                doc = XDocument.Load(stream);

            var root = doc.Root ?? throw new InvalidDataException("modDesc.xml hat kein Root-Element");
            var metadata = ParseMetadata(root);
            var previewBytes = TryExtractPreview(archive, metadata.IconFileName);
            return new ModReadResult(metadata, previewBytes, null);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Konnte modDesc.xml nicht lesen: {Path}", zipPath);
            return new ModReadResult(null, null, ex.Message);
        }
    }

    public static bool IsModZip(string zipPath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(zipPath);
            return archive.GetEntry("modDesc.xml") is not null;
        }
        catch { return false; }
    }

    private static ModMetadata ParseMetadata(XElement root)
    {
        var descVersion = int.TryParse((string?)root.Attribute("descVersion"), out var v) ? v : 0;
        var author = (string?)root.Element("author") ?? "";
        var version = (string?)root.Element("version") ?? "";
        var iconFile = (string?)root.Element("iconFilename");
        var multiplayer = string.Equals(
            (string?)root.Element("multiplayer")?.Attribute("supported"),
            "true", StringComparison.OrdinalIgnoreCase);

        var title = PickLocalized(root.Element("title")) ?? Path.GetFileNameWithoutExtension(iconFile ?? "");
        var description = PickLocalized(root.Element("description")) ?? "";

        return new ModMetadata(
            Title: title.Trim(),
            Author: author.Trim(),
            Version: version.Trim(),
            Description: description.Trim(),
            IconFileName: string.IsNullOrWhiteSpace(iconFile) ? null : iconFile,
            MultiplayerSupported: multiplayer,
            DescVersion: descVersion);
    }

    private static string? PickLocalized(XElement? node)
    {
        if (node is null) return null;
        foreach (var lang in LanguagePreference)
        {
            var e = node.Element(lang);
            if (e is not null && !string.IsNullOrWhiteSpace(e.Value))
                return e.Value;
        }
        var first = node.Elements().FirstOrDefault();
        if (first is not null && !string.IsNullOrWhiteSpace(first.Value))
            return first.Value;
        return string.IsNullOrWhiteSpace(node.Value) ? null : node.Value;
    }

    /// <summary>
    /// Sucht ein Vorschau-PNG in der ZIP. Reihenfolge:
    /// 1. iconFilename mit .png (statt .dds), 2. icon.png, 3. store_*.png,
    /// 4. beliebiges *.png, 5. iconFilename als DDS (dekodiert),
    /// 6. beliebiges *.dds (dekodiert). DDS ist Fallback — echte PNGs sind
    /// meistens bessere Store-Bilder, DDS ist typisch das in-Game-Icon.
    /// </summary>
    private static byte[]? TryExtractPreview(ZipArchive archive, string? iconFileName)
    {
        var pngCandidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(iconFileName))
        {
            var withoutExt = Path.GetFileNameWithoutExtension(iconFileName);
            pngCandidates.Add(withoutExt + ".png");
            if (iconFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                pngCandidates.Add(iconFileName);
        }
        pngCandidates.Add("icon.png");

        foreach (var name in pngCandidates)
        {
            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, name, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
            {
                var png = ReadIfImage(entry);
                if (png is not null) return png;
            }
        }

        var store = archive.Entries.FirstOrDefault(e =>
            e.FullName.StartsWith("store_", StringComparison.OrdinalIgnoreCase) &&
            e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        if (store is not null)
        {
            var png = ReadIfImage(store);
            if (png is not null) return png;
        }

        var anyPng = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".png", StringComparison.OrdinalIgnoreCase));
        if (anyPng is not null)
        {
            var png = ReadIfImage(anyPng);
            if (png is not null) return png;
        }

        // Fallback: DDS dekodieren. Erst der genannte iconFilename, dann beliebige *.dds.
        // Pfim macht das alles in-Memory; die Konvertierung ist billig (~10 ms für 256px).
        if (!string.IsNullOrWhiteSpace(iconFileName) &&
            iconFileName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
        {
            var namedDds = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName, iconFileName, StringComparison.OrdinalIgnoreCase));
            var converted = namedDds is null ? null : DdsToPngConverter.Convert(ReadBytes(namedDds));
            if (converted is not null) return converted;
        }

        var anyDds = archive.Entries.FirstOrDefault(e =>
            e.FullName.EndsWith(".dds", StringComparison.OrdinalIgnoreCase));
        if (anyDds is not null)
        {
            var converted = DdsToPngConverter.Convert(ReadBytes(anyDds));
            if (converted is not null) return converted;
        }

        return null;
    }

    /// <summary>
    /// Liest die Bytes und verifiziert per Magic-Bytes, dass es wirklich PNG
    /// oder JPG ist — schützt gegen Mods, die eine DDS-Datei fälschlich unter
    /// einem <c>.png</c>-Namen ablegen.
    /// </summary>
    private static byte[]? ReadIfImage(ZipArchiveEntry entry)
    {
        var bytes = ReadBytes(entry);
        if (IsPngOrJpeg(bytes)) return bytes;
        Log.Debug("Datei {n} sieht nicht wie PNG/JPG aus — überspringe.", entry.FullName);
        return null;
    }

    private static bool IsPngOrJpeg(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
            return true; // PNG
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF)
            return true; // JPEG
        return false;
    }

    private static byte[] ReadBytes(ZipArchiveEntry entry)
    {
        using var ms = new MemoryStream();
        using var s = entry.Open();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}

public sealed record ModReadResult(ModMetadata? Metadata, byte[]? PreviewPngBytes, string? Error);
