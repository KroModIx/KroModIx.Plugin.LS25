using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Plugin-lokale Einstellungen (Ollama-Endpoint, Modell). Persistiert in
/// <c>settings.json</c> im PluginDataDir. Atomar schreiben (tmp + move),
/// damit ein Absturz während Save keine korrupte Datei hinterlässt.
/// </summary>
public sealed class Ls25SettingsService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _settingsFile;
    private Ls25Settings _current;

    public Ls25SettingsService(Ls25Paths paths)
    {
        _settingsFile = Path.Combine(paths.PluginDataDir, "settings.json");
        _current = Load();
    }

    public Ls25Settings Current => _current;

    public void Update(Action<Ls25Settings> mutate)
    {
        mutate(_current);
        Save(_current);
    }

    private Ls25Settings Load()
    {
        try
        {
            if (!File.Exists(_settingsFile)) return new Ls25Settings();
            var json = File.ReadAllText(_settingsFile);
            return JsonSerializer.Deserialize<Ls25Settings>(json, JsonOptions) ?? new Ls25Settings();
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Settings-Load fehlgeschlagen — starte mit Defaults");
            return new Ls25Settings();
        }
    }

    private void Save(Ls25Settings s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFile)!);
            var tmp = _settingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(s, JsonOptions));
            if (File.Exists(_settingsFile)) File.Delete(_settingsFile);
            File.Move(tmp, _settingsFile);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Settings-Save fehlgeschlagen");
        }
    }
}

public sealed class Ls25Settings
{
    /// <summary>Ab welchem Cache-Alter der ModHub-Katalog beim App-Start
    /// erneut geladen wird (in Stunden). 0 = immer neu laden. Standalone-
    /// Default und hier: 24 h. Manueller Refresh-Button umgeht den Check.</summary>
    public int CatalogRefreshHours { get; set; } = 24;
}
