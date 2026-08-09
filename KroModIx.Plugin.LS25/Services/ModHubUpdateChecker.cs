using System.Collections.Generic;
using System.Linq;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Zählt „neue" Mod-Einträge im ModHub-Katalog seit dem letzten Besuch der
/// Katalog-Tab (siehe <see cref="CatalogCache.SaveSeenSnapshot"/>). Wird vom
/// <see cref="Ls25Plugin"/>-IUpdateNotifier verwendet — der Host rendert das
/// als grünen ↑-Badge auf der FS25-Kachel.
///
/// <para>Kein API-Aufruf im Notifier: er liest nur den bereits gecachten
/// Katalog + den Seen-Snapshot. Der Katalog wird vom User via ModHub-Tab
/// gepflegt (bzw. periodisch beim Öffnen); der Notifier ist rein passiv.</para>
///
/// <para>Ohne Katalog-Cache: 0. Ohne Seen-Snapshot: 0 (der User hat den
/// Katalog noch nie besucht — der Badge würde beim ersten Fresh-Load die
/// gesamte Katalog-Größe anzeigen, das wäre irreführend als „Updates").</para>
/// </summary>
public sealed class ModHubUpdateChecker
{
    private readonly CatalogCache _cache;

    public ModHubUpdateChecker(CatalogCache cache) => _cache = cache;

    public int CountUnseen(string language)
    {
        var snapshot = _cache.Load(language);
        var seen = _cache.LoadSeenSnapshot(language);
        if (snapshot is null || seen is null) return 0;
        return snapshot.Entries.Count(e => !seen.Contains(e.DetailUrl));
    }
}
