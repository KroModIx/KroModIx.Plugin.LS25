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
    string? ReadError)
{
    /// <summary>v1.22.0: gesetzt, wenn dieser Eintrag einem <b>anderen
    /// Mod-Manager</b> gehört (lmm, r2modman, Vortex, SMM/ficsit). Erkannt am
    /// Verweis, nicht am Namen — siehe
    /// <see cref="KroModIx.Plugin.Contracts.ForeignManagerDetection"/>.
    /// Solche Einträge werden gelistet, damit der Nutzer sieht was im Spiel
    /// liegt, aber nicht verändert: am 04.10.2026 hat ein
    /// Deinstallieren-Klick im Icarus-Plugin lmms Pak entfernt und damit
    /// lautlos eine Mod aus dem Spiel genommen, während ihre Quelle woanders
    /// unversehrt lag.</summary>
    public string? ManagedBy { get; init; }

    /// <summary>Ob das Plugin diesen Eintrag verändern darf.</summary>
    public bool CanModify => ManagedBy is null;

    /// <summary>Einmalige Erkennung beim Scan — der Scanner legt sie über
    /// seine Ergebnisliste. Als Methode und nicht als berechnete Eigenschaft,
    /// weil sonst jede Bindung in der Oberfläche einen Dateisystem-Zugriff
    /// auslöst.</summary>
    public InstalledMod MitVerwalterErkennung()
        => KroModIx.Plugin.Contracts.ForeignManagerDetection.IsForeignManaged(FilePath, out var wer)
            ? this with { ManagedBy = wer }
            : this;
}

