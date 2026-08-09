using System;
using System.IO;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Findet für den <see cref="DetectedGame"/> den <c>mods/</c>-Ordner von LS25.
/// Der Host löst schon <see cref="DetectedGame.UserDataDir"/> (Proton-User-Docs
/// unter Linux, Documents/My Games unter Windows) — wir hängen nur den Spiel-
/// Ordner + <c>mods/</c> dran.
/// </summary>
public sealed class Ls25PathResolver
{
    private const string GameFolderName = "FarmingSimulator2025";
    private const string ModsSubdir = "mods";

    /// <summary>Ermittelt den Mod-Ordner. Rückgabe ist der Pfad, auch wenn er
    /// noch nicht existiert — LS25 legt ihn beim ersten Mod-Install selbst an.
    /// Wenn kein sinnvoller Pfad ableitbar ist: <c>null</c>.</summary>
    public string? GetModsDir(DetectedGame game)
    {
        if (OperatingSystem.IsWindows())
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrEmpty(docs))
                return Path.Combine(docs, "My Games", GameFolderName, ModsSubdir);
            return null;
        }

        // Linux: UserDataDir kommt vom Host (Proton-Docs-Pfad). Falls null, fallback
        // auf native Home-Location (theoretisch für einen späteren Linux-Port).
        if (game.UserDataDir is string udd && Directory.Exists(udd))
        {
            var myGames = Path.Combine(udd, "My Games", GameFolderName);
            if (Directory.Exists(myGames))
                return Path.Combine(myGames, ModsSubdir);
            // Neue Installation: legen wir bei Bedarf an.
            return Path.Combine(udd, "My Games", GameFolderName, ModsSubdir);
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home))
            return Path.Combine(home, ".local", "share", GameFolderName, ModsSubdir);
        return null;
    }
}
