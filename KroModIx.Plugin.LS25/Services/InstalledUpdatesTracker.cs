using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>Persistente Zählung installierter Mods mit verfügbarem Update
/// aus <c>InstalledModsViewModel.CheckUpdatesAsync</c>. Wird vom
/// <c>Ls25Plugin.GetPendingUpdatesAsync</c> gelesen und in den Sidebar-
/// Kachel-Badge eingerechnet.
///
/// <para>Analog zu <c>ModHubUpdateChecker</c> (zählt neue Katalog-Einträge)
/// — zwei separate Signale im Badge kombiniert.</para></summary>
public sealed class InstalledUpdatesTracker
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    private readonly string _cachePath;
    private Payload _state;

    public InstalledUpdatesTracker(Ls25Paths paths)
    {
        var cacheDir = paths.CatalogCacheDir;
        Directory.CreateDirectory(cacheDir);
        _cachePath = Path.Combine(cacheDir, "installed-updates.json");
        _state = Load() ?? new Payload();
    }

    public int PendingCount => _state.Count;
    public string Summary => _state.Summary ?? "";
    public DateTime? LastCheckedUtc => _state.LastCheckedUtc;

    public void SetPending(int count, string summary)
    {
        _state = new Payload
        {
            Count = count,
            Summary = summary,
            LastCheckedUtc = DateTime.UtcNow,
        };
        Save();
    }

    private Payload? Load()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            return JsonSerializer.Deserialize<Payload>(File.ReadAllText(_cachePath));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "InstalledUpdatesTracker-Load fehlgeschlagen");
            return null;
        }
    }

    private void Save()
    {
        try
        {
            var tmp = _cachePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_state));
            if (File.Exists(_cachePath)) File.Delete(_cachePath);
            File.Move(tmp, _cachePath);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "InstalledUpdatesTracker-Save fehlgeschlagen");
        }
    }

    private sealed class Payload
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("lastCheckedUtc")] public DateTime? LastCheckedUtc { get; set; }
    }
}
