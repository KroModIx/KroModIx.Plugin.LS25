using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using KroModIx.Plugin.Contracts;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>Prüft für jeden installierten LS25-Mod ob im ModHub-Katalog eine
/// neuere Version steht. Fuzzy-Match Filename ↔ Katalog-Titel, dann
/// <c>ModHubService.FetchModDetailAsync</c> für die aktuelle Version.
/// Schreibt Ergebnis in <see cref="InstalledUpdatesTracker"/> für Sidebar-
/// Kachel-Badge.
///
/// <para>Analog Icarus + Satisfactory-Pattern: gemeinsame Check-Logik
/// zwischen User-Klick im VM und Auto-Trigger beim App-Start.</para></summary>
public sealed class InstalledUpdatesChecker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private const string Language = "de";

    private readonly ModInstallService _installer;
    private readonly ModHubService _hub;
    private readonly CatalogCache _cache;
    private readonly InstalledUpdatesTracker _tracker;

    public InstalledUpdatesChecker(ModInstallService installer, ModHubService hub,
        CatalogCache cache, InstalledUpdatesTracker tracker)
    {
        _installer = installer;
        _hub = hub;
        _cache = cache;
        _tracker = tracker;
    }

    public async Task<int> CheckAsync(
        Action<string, string, string>? onUpdateFound = null, // (fileName, oldVer, newVer)
        Action<string>? onProgress = null,
        CancellationToken ct = default)
    {
        var snapshot = _cache.Load(Language);
        if (snapshot is null || snapshot.Entries.Count == 0)
        {
            Log.Debug("Kein Katalog-Cache — Update-Check skipped");
            _tracker.SetPending(0, "");
            return 0;
        }

        var mods = _installer.ListInstalled().ToList();
        int checkedCount = 0, updatedCount = 0;

        foreach (var mod in mods)
        {
            if (ct.IsCancellationRequested) break;
            var installedVersion = mod.Metadata?.Version;
            if (string.IsNullOrWhiteSpace(installedVersion)) continue;

            var catalogEntry = LookupCatalogEntry(snapshot.Entries, mod.FileName);
            if (catalogEntry is null) continue;
            var modId = ExtractModIdFromUrl(catalogEntry.DetailUrl);
            if (modId is null) continue;

            checkedCount++;
            onProgress?.Invoke($"Updates prüfen: {checkedCount} · {mod.Metadata?.Title ?? mod.FileName}");
            try
            {
                var detail = await _hub.FetchModDetailAsync(modId.Value, Language);
                if (detail is null || string.IsNullOrWhiteSpace(detail.Version)) continue;
                if (IsVersionNewer(detail.Version, installedVersion))
                {
                    onUpdateFound?.Invoke(mod.FileName, installedVersion, detail.Version);
                    updatedCount++;
                    Log.Info("Update verfügbar {File}: {Old} → {New}",
                        mod.FileName, installedVersion, detail.Version);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Update-Check für mod_id={Id} fehlgeschlagen", modId);
            }
        }

        var summary = updatedCount > 0
            ? $"{updatedCount} Mod-Update(s) verfügbar (von {checkedCount} geprüft)"
            : "";
        _tracker.SetPending(updatedCount, summary);
        Log.Info("LS25 Update-Check fertig: {Updated}/{Checked}", updatedCount, checkedCount);
        return updatedCount;
    }

    private static ModHubEntry? LookupCatalogEntry(IReadOnlyList<ModHubEntry> catalog, string zipFileName)
    {
        var normalized = NormalizeForMatch(Path.GetFileNameWithoutExtension(zipFileName));
        if (normalized.Length < 3) return null;
        foreach (var e in catalog)
        {
            var titleNorm = NormalizeForMatch(e.Title);
            if (titleNorm.Length < 3) continue;
            if (normalized.Contains(titleNorm) || titleNorm.Contains(normalized))
                return e;
        }
        return null;
    }

    private static string NormalizeForMatch(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        var result = sb.ToString();
        foreach (var prefix in new[] { "fs25", "fs22", "ls25", "ls22" })
            if (result.StartsWith(prefix)) result = result.Substring(prefix.Length);
        if (result.EndsWith("disabled")) result = result.Substring(0, result.Length - "disabled".Length);
        return result;
    }

    private static int? ExtractModIdFromUrl(string url)
    {
        var m = Regex.Match(url, @"mod_id=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out var id) ? id : null;
    }

    /// <summary>Delegiert an den Contracts-Baukasten (v1.27). Die frueheren
    /// zwei Eigenbau-Kopien im Repo bauten auf Version.TryParse und gaben bei
    /// unparsebarem Format still false zurueck — Mod-Versionen wie "3",
    /// "1.2.3b" oder "2.0-hotfix" bekamen damit NIE ein Update gemeldet.</summary>
    private static bool IsVersionNewer(string catalogVersion, string installedVersion)
        => VersionCompare.IsNewer(catalogVersion, installedVersion);
}
