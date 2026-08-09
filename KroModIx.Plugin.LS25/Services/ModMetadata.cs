namespace KroModIx.Plugin.LS25.Services;

/// <summary>Aus modDesc.xml gelesene Mod-Metadaten. Übernommen aus
/// LS-ModManager mit identischer Struktur.</summary>
public sealed record ModMetadata(
    string Title,
    string Author,
    string Version,
    string Description,
    string? IconFileName,
    bool MultiplayerSupported,
    int DescVersion);
