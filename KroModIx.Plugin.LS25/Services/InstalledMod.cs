using System;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>Ein im Mod-Ordner liegender LS25-Mod. FilePath endet auf `.zip`
/// (aktiv) oder `.zip.disabled` (inaktiv, LS25 ignoriert die Datei).</summary>
public sealed record InstalledMod(
    string FilePath,
    string FileName,
    long FileSizeBytes,
    DateTime InstalledUtc,
    bool IsEnabled,
    ModMetadata? Metadata,
    string? ReadError);
