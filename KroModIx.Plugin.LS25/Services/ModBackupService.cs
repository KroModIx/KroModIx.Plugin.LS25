using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>
/// Erstellt und liest Backup-Archive der aktuellen Mod-Konfiguration.
///
/// <para><b>Format:</b> ein ZIP-Archiv mit <c>manifest.json</c> und einem
/// <c>mods/</c>-Unterordner, der alle Mod-ZIPs im Original enthält (auch
/// deaktivierte — Endung im Manifest markiert). Damit lässt sich der Zustand
/// des Mod-Ordners exakt rekonstruieren, ohne dass beim Restore Internet
/// nötig wäre.</para>
///
/// <para>Adaptiert aus LS-ModManager v1.5 — statt ModPathService nutzt das
/// Plugin den Mods-Pfad direkt aus <see cref="ModInstallService.ModsDir"/>.</para>
/// </summary>
public sealed class ModBackupService
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();

    public const int CurrentFormatVersion = 1;

    private const string ManifestEntryName = "manifest.json";
    private const string ModsFolderPrefix = "mods/";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ModInstallService _install;

    public ModBackupService(ModInstallService install)
    {
        _install = install;
    }

    public async Task<BackupResult> CreateBackupAsync(
        string targetZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var mods = _install.ListInstalled();
        if (mods.Count == 0)
            throw new InvalidOperationException("Keine installierten Mods vorhanden — nichts zu sichern.");

        Directory.CreateDirectory(Path.GetDirectoryName(targetZipPath)!);

        var tmpPath = targetZipPath + ".tmp";
        if (File.Exists(tmpPath)) File.Delete(tmpPath);

        var manifest = new BackupManifest(
            Version: CurrentFormatVersion,
            CreatedUtc: DateTime.UtcNow,
            AppVersion: typeof(ModBackupService).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            Mods: mods.Select(m => new BackupManifestEntry(
                FileName: m.FileName,
                IsEnabled: m.IsEnabled,
                ModVersion: m.Metadata?.Version,
                Author: m.Metadata?.Author,
                Title: m.Metadata?.Title)).ToList());

        await Task.Run(() =>
        {
            using var fs = File.Create(tmpPath);
            using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

            var manifestEntry = archive.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, JsonOptions));

            for (var i = 0; i < mods.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var mod = mods[i];
                // Mod-ZIPs sind bereits komprimiert — NoCompression spart CPU.
                var modEntry = archive.CreateEntry(ModsFolderPrefix + mod.FileName,
                    CompressionLevel.NoCompression);
                using (var entryStream = modEntry.Open())
                using (var srcStream = File.OpenRead(mod.FilePath))
                    srcStream.CopyTo(entryStream);
                progress?.Report(new BackupProgress(i + 1, mods.Count, mod.FileName));
            }
        }, ct).ConfigureAwait(false);

        // Move mit overwrite: sonst ist ein bestehendes Backup schon
        // geloescht, wenn das Move scheitert (Platte voll, Datei gesperrt).
        File.Move(tmpPath, targetZipPath, overwrite: true);

        var fileInfo = new FileInfo(targetZipPath);
        Log.Info("Backup erstellt: {p} ({n} Mods, {size} Bytes)",
            targetZipPath, mods.Count, fileInfo.Length);
        return new BackupResult(targetZipPath, mods.Count, fileInfo.Length);
    }

    public static BackupManifest ReadManifest(string backupZipPath)
    {
        using var archive = ZipFile.OpenRead(backupZipPath);
        var entry = archive.GetEntry(ManifestEntryName)
            ?? throw new InvalidDataException($"Backup enthält kein {ManifestEntryName}.");
        using var reader = new StreamReader(entry.Open());
        var manifest = JsonSerializer.Deserialize<BackupManifest>(reader.ReadToEnd(), JsonOptions)
            ?? throw new InvalidDataException("Manifest ist leer oder nicht lesbar.");
        if (manifest.Version != CurrentFormatVersion)
            throw new InvalidDataException(
                $"Unbekannte Backup-Format-Version: {manifest.Version} (Plugin unterstützt {CurrentFormatVersion}).");
        return manifest;
    }

    public async Task<RestoreResult> RestoreBackupAsync(
        string backupZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(backupZipPath))
            throw new FileNotFoundException("Backup-Datei existiert nicht", backupZipPath);

        var modPath = _install.ModsDir;
        Directory.CreateDirectory(modPath);

        var manifest = ReadManifest(backupZipPath);
        var restored = 0;
        var skipped = 0;

        var tmpDir = Path.Combine(Path.GetTempPath(), $"ls25-restore-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tmpDir);
        try
        {
            await Task.Run(() =>
            {
                using var archive = ZipFile.OpenRead(backupZipPath);
                for (var i = 0; i < manifest.Mods.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var meta = manifest.Mods[i];
                    var entry = archive.GetEntry(ModsFolderPrefix + meta.FileName);
                    if (entry is null)
                    {
                        Log.Warn("Restore: Mod-ZIP fehlt im Backup: {n}", meta.FileName);
                        skipped++;
                        progress?.Report(new BackupProgress(i + 1, manifest.Mods.Count, meta.FileName));
                        continue;
                    }

                    // Filename beim Extract auf .zip normalisieren — sonst würde
                    // Install ein .zip.disabled ins Mods-Verzeichnis kopieren und
                    // SetEnabled(false) danach nochmal .disabled anhängen.
                    var normalizedName = meta.FileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? meta.FileName.Substring(0, meta.FileName.Length - ".disabled".Length)
                        : meta.FileName;
                    var tmpZip = Path.Combine(tmpDir, normalizedName);
                    entry.ExtractToFile(tmpZip, overwrite: true);

                    try
                    {
                        var installed = _install.Install(tmpZip, overwrite: true);
                        if (!meta.IsEnabled)
                            _install.SetEnabled(installed, enabled: false);
                        restored++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn(ex, "Restore-Install übersprungen: {n}", meta.FileName);
                        skipped++;
                    }
                    finally
                    {
                        try { File.Delete(tmpZip); } catch { /* best-effort */ }
                    }

                    progress?.Report(new BackupProgress(i + 1, manifest.Mods.Count, meta.FileName));
                }
            }, ct).ConfigureAwait(false);
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { /* best-effort */ }
        }

        Log.Info("Restore fertig: {r} wiederhergestellt, {s} übersprungen", restored, skipped);
        return new RestoreResult(restored, skipped, manifest);
    }
}

public sealed record BackupManifest(
    int Version,
    DateTime CreatedUtc,
    string AppVersion,
    List<BackupManifestEntry> Mods);

public sealed record BackupManifestEntry(
    string FileName,
    bool IsEnabled,
    string? ModVersion,
    string? Author,
    string? Title);

public sealed record BackupProgress(int Current, int Total, string CurrentFileName)
{
    public double Fraction => Total == 0 ? 0 : (double)Current / Total;
}

public sealed record BackupResult(string FilePath, int ModCount, long FileSizeBytes);

public sealed record RestoreResult(int RestoredCount, int SkippedCount, BackupManifest Manifest);
