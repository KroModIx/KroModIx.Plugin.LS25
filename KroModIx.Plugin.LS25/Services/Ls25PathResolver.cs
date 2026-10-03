using System;
using System.Collections.Generic;
using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Findet für den <see cref="DetectedGame"/> den <c>mods/</c>-Ordner von LS25.
/// Der Host löst schon <see cref="DetectedGame.UserDataDir"/> (Proton-User-Docs
/// unter Linux, Documents/My Games unter Windows) — wir hängen nur den Spiel-
/// Ordner + <c>mods/</c> dran.
///
/// <para><b>v1.21.0:</b> die Auflösung läuft über
/// <see cref="ModFolderDiscovery"/> statt über feste
/// <c>Path.Combine</c>-Ketten. Zwei Gründe, beide gemessen:</para>
///
/// <list type="number">
/// <item><b>Der Ordner fehlt.</b> LS25 legt <c>mods/</c> erst beim ersten
///   Mod-Install selbst an. Real vorgefunden: der Prefix-Pfad
///   <c>…/My Documents/My Games/FarmingSimulator2025/</c> existierte samt 20
///   Savegames, nur <c>mods/</c> darin nicht. Das Plugin meldete
///   „Mods-Ordner existiert nicht", zeigte eine leere Liste — und installieren
///   ging auch nicht, weil das Ziel fehlte.</item>
/// <item><b>Groß-/Kleinschreibung.</b> Wine schreibt je nach Version
///   <c>My Documents</c> oder <c>Documents</c>, und <c>mods</c> gegen
///   <c>Mods</c> unterscheidet Linux sehr wohl. Jedes Segment wird jetzt
///   einzeln gegen die echten Verzeichniseinträge geprüft.</item>
/// </list>
/// </summary>
public sealed class Ls25PathResolver
{
    private const string GameFolderName = "FarmingSimulator2025";

    /// <summary>Relativ zur jeweiligen Dokumente-Wurzel. Der erste Eintrag ist
    /// der kanonische — er wird angelegt, wenn keiner existiert.</summary>
    private static readonly string[] ModDirCandidates =
    {
        "My Games/" + GameFolderName + "/mods",
        "My Games/" + GameFolderName + "/Mods",
    };

    /// <summary>Der vorhandene Mod-Ordner, oder null. Legt nichts an.</summary>
    public string? GetModsDir(DetectedGame game) => Resolve(game, create: false);

    /// <summary>Der Mod-Ordner, angelegt falls noch keine Variante existiert.
    /// Null nur, wenn sich keine Dokumente-Wurzel ableiten lässt oder das
    /// Anlegen scheitert (Rechte, read-only Mount).</summary>
    public string? EnsureModsDir(DetectedGame game) => Resolve(game, create: true);

    private static string? Resolve(DetectedGame game, bool create)
    {
        foreach (var root in EnumerateDocumentRoots(game))
        {
            var hit = create
                ? ModFolderDiscovery.FindOrCreate(root, ModDirCandidates)
                : ModFolderDiscovery.Find(root, ModDirCandidates);
            if (hit is not null) return hit;
        }
        return null;
    }

    /// <summary>Die möglichen Dokumente-Wurzeln in Prioritätsreihenfolge.
    /// Nur existierende — <see cref="ModFolderDiscovery"/> legt sonst eine
    /// Ordnerkette an einem Ort an, den es gar nicht geben sollte.</summary>
    private static IEnumerable<string> EnumerateDocumentRoots(DetectedGame game)
    {
        // 1. Was der Host abgeleitet hat (Proton-Docs unter Linux,
        //    Documents unter Windows). Der Normalfall.
        if (!string.IsNullOrEmpty(game.UserDataDir) && Directory.Exists(game.UserDataDir))
            yield return game.UserDataDir;

        // 2. Proton-Prefix direkt, falls UserDataDir nicht gefüllt ist. Beide
        //    Wine-Schreibweisen, weil die je nach Proton-Version wechselt.
        if (!string.IsNullOrEmpty(game.ProtonPrefix))
        {
            var users = Path.Combine(game.ProtonPrefix, "drive_c", "users", "steamuser");
            foreach (var docs in new[] { "My Documents", "Documents" })
            {
                var candidate = Path.Combine(users, docs);
                if (Directory.Exists(candidate)) yield return candidate;
            }
        }

        // 3. Native Dokumente (Windows, und ein theoretischer Linux-Port).
        var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrEmpty(myDocs) && Directory.Exists(myDocs))
            yield return myDocs;
    }
}
