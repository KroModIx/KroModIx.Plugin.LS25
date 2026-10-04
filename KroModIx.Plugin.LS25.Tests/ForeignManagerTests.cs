using System;
using System.IO;
using FluentAssertions;
using KroModIx.Plugin.LS25.Services;
using Xunit;

namespace KroModIx.Plugin.LS25.Tests;

/// <summary>Mods, die ein anderer Mod-Manager ausgeliefert hat. LS25 ist
/// davon betroffen, weil der LS-ModManager und lmm in denselben
/// <c>mods/</c>-Ordner liefern.
///
/// <para>Der Anlass ist bezahlt, nur in einem anderen Plugin: am 04.10.2026
/// hat ein Deinstallieren-Klick im Icarus-Plugin lmms zusammengeführtes Pak
/// entfernt und damit lautlos eine Mod aus dem Spiel genommen — die Quelle
/// lag unversehrt in lmms Zwischenspeicher.</para></summary>
public sealed class ForeignManagerTests : IDisposable
{
    private readonly string _tmp = Directory.CreateTempSubdirectory("fremd-ls25").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* Aufräumen darf scheitern */ }
    }

    private string EigeneMod(string name)
    {
        var p = Path.Combine(_tmp, name);
        File.WriteAllText(p, "zip");
        return p;
    }

    /// <summary>So liefert lmm aus: ein Verweis in den eigenen
    /// Zwischenspeicher.</summary>
    private string FremdeMod(string name)
    {
        var cache = Path.Combine(_tmp, "share", "lmm", "cache");
        Directory.CreateDirectory(cache);
        var ziel = Path.Combine(cache, name);
        File.WriteAllText(ziel, "zip");
        var link = Path.Combine(_tmp, name);
        File.CreateSymbolicLink(link, ziel);
        return link;
    }

    private static InstalledMod Erkenne(string pfad)
        => new InstalledMod(pfad, Path.GetFileName(pfad), 1, DateTime.UtcNow, true, null, null)
            .MitVerwalterErkennung();

    [Fact]
    public void Eine_eigene_Mod_bleibt_veraenderbar()
    {
        var mod = Erkenne(EigeneMod("FS25_MeinMod.zip"));

        mod.CanModify.Should().BeTrue();
        mod.ManagedBy.Should().BeNull();
    }

    [Fact]
    public void Eine_fremd_ausgelieferte_Mod_wird_erkannt_und_benannt()
    {
        var mod = Erkenne(FremdeMod("FS25_FremdMod.zip"));

        mod.CanModify.Should().BeFalse();
        mod.ManagedBy.Should().Be("lmm");
    }
}
