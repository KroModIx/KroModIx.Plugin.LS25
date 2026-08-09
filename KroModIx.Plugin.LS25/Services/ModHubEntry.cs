using System;
using System.Collections.Generic;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>Ein Eintrag aus dem GIANTS-ModHub-Katalog.</summary>
public sealed record ModHubEntry(
    string Title,
    string Author,
    string Category,
    string PreviewUrl,
    string DetailUrl,
    string? Version,
    string? SizeText,
    string Source = ModHubEntry.GiantsSource,
    bool CanInAppDownload = true,
    bool IsFeatured = false)
{
    public const string GiantsSource = "GiantsModHub";
    public const string ModhosterSource = "Modhoster";
    public const string HofHirschfeldSource = "Hof Hirschfeld";
}

/// <summary>GIANTS-Kategorie: URL-Key (<c>filter=xxx</c>) + lokalisiertes Label.</summary>
public sealed record ModHubCategory(string Filter, string Label);

/// <summary>Vollständige Detail-Ansicht eines ModHub-Mods (aus mod.php gescrapt).</summary>
public sealed record ModHubDetail(
    int ModId,
    string Title,
    string Author,
    string Category,
    string Version,
    string SizeText,
    string ReleaseDate,
    string Platform,
    string Filename,
    string RatingText,
    string DescriptionText,
    IReadOnlyList<string> ScreenshotUrls,
    string DownloadUrl,
    string DetailUrl);

/// <summary>Ergebnis eines abgeschlossenen Downloads (Zielpfad im Downloads-Ordner).</summary>
public sealed record ModDownloadResult(string TargetZipPath, string FileName);

/// <summary>Progress-Report während des Downloads.</summary>
public sealed record ModDownloadProgress(long BytesDone, long? BytesTotal, string FileName)
{
    public double? Fraction => BytesTotal is > 0 ? (double)BytesDone / BytesTotal.Value : null;

    public string FormatShort() =>
        BytesTotal is > 0
            ? $"{BytesDone / (1024d * 1024d):F1} / {BytesTotal.Value / (1024d * 1024d):F1} MB"
            : $"{BytesDone / (1024d * 1024d):F1} MB";
}

public sealed record CatalogSnapshot(
    DateTime SavedUtc,
    string Language,
    List<ModHubEntry> Entries);
