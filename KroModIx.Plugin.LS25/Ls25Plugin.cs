using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.LS25.Services;
using KroModIx.Plugin.LS25.Views;

namespace KroModIx.Plugin.LS25;

public sealed class Ls25Plugin : IGameModPlugin, IUpdateNotifier
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.ls25",
        DisplayName: "Landwirtschafts-Simulator 25",
        Version: "1.7.0",
        Author: "Kroste",
        Description: "Mod-Manager für Farming Simulator 25 — Kroste-Card-Look. Per-Row-Buttons, Cover, INSTALLIERT- und ⭐ EMPFOHLEN-Badges, Spielstart via Steam, Mod-Updates, Detail-Dialog, aggregierter ModHub, Backup/Restore, KI-Zusammenfassung, grüner ↑-Badge auf der FS25-Kachel bei neuen ModHub-Einträgen (IUpdateNotifier).");

    public IReadOnlyList<GameTarget> Targets { get; } = new[]
    {
        new GameTarget(
            "farming-simulator-25", "Farming Simulator 25",
            SteamAppId: 2300320,
            AlternativeExecutableNames: new[] { "FarmingSimulator2025.exe" },
            Platforms: Platforms.Both),
    };

    private IHostServices? _host;
    private Ls25Paths? _paths;
    private Ls25SettingsService? _settings;
    private ModHubService? _hub;
    private HofHirschfeldCatalogService? _hofHirschfeld;
    private ModhosterCatalogService? _modhoster;
    private CatalogCache? _cache;
    private ModPreviewService? _previews;
    private DownloadEventBus? _downloadBus;
    private ModHubUpdateChecker? _updateChecker;
    private InstalledUpdatesTracker? _installedUpdatesTracker;
    private IReadOnlyList<DetectedGame> _activatedGames = Array.Empty<DetectedGame>();
    private readonly Dictionary<string, ModInstallService> _installers = new();
    private readonly Dictionary<string, ModBackupService> _backups = new();
    private readonly Dictionary<string, InstalledUpdatesChecker> _updateCheckers = new();
    private readonly ModDescReader _reader = new();
    private readonly Ls25PathResolver _pathResolver = new();

    public Task InitializeAsync(IHostServices host, IReadOnlyList<DetectedGame> activatedGames, CancellationToken ct)
    {
        _host = host;
        _paths = new Ls25Paths(host);
        _settings = new Ls25SettingsService(_paths);
        _cache = new CatalogCache(_paths);
        _hub = new ModHubService(_paths, host.CreateHttpClient("modhub"));
        _hofHirschfeld = new HofHirschfeldCatalogService(host.CreateHttpClient("hofhirschfeld"));
        _modhoster = new ModhosterCatalogService(host.CreateHttpClient("modhoster"));
        _previews = new ModPreviewService(_paths, _reader, host.CreateHttpClient("previews"));
        _downloadBus = new DownloadEventBus();
        _updateChecker = new ModHubUpdateChecker(_cache);
        _installedUpdatesTracker = new InstalledUpdatesTracker(_paths);
        _activatedGames = activatedGames;

        foreach (var game in activatedGames)
        {
            var modsDir = _pathResolver.GetModsDir(game);
            if (modsDir is null)
            {
                host.Logger.Warn("LS25: konnte keinen Mods-Pfad für {Game} ableiten", game.Target.DisplayName);
                continue;
            }
            var installer = new ModInstallService(modsDir, _reader, _paths);
            _installers[game.Target.GameId] = installer;
            _backups[game.Target.GameId] = new ModBackupService(installer);
            _updateCheckers[game.Target.GameId] = new InstalledUpdatesChecker(
                installer, _hub, _cache, _installedUpdatesTracker);
            host.Logger.Info("LS25 initialisiert: Mods-Ordner = {Path}", modsDir);
        }

        // Auto-Check für installierte Mod-Updates im Hintergrund — Sidebar-
        // Kachel-Badge sofort nach Plugin-Load sichtbar. 20 s Delay: nach LS25
        // 30 s brauchts länger weil der Katalog beim ersten Start fetched wird
        // (7000+ Einträge). Der Check nutzt den existierenden Cache und macht
        // dann Detail-Fetches nur für Mods die den Fuzzy-Match überleben.
        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            foreach (var checker in _updateCheckers.Values)
            {
                try { await checker.CheckAsync(ct: ct); }
                catch (Exception ex) { host.Logger.Debug(ex, "LS25 Auto-Update-Check fehlgeschlagen"); }
            }
        }, ct);

        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (!_installers.TryGetValue(game.Target.GameId, out var installer) || _host is null
            || _hub is null || _cache is null || _hofHirschfeld is null || _modhoster is null
            || _paths is null || _settings is null || _previews is null || _downloadBus is null
            || !_backups.TryGetValue(game.Target.GameId, out var backup)
            || !_updateCheckers.TryGetValue(game.Target.GameId, out var updatesChecker))
            yield break;

        yield return new InstalledTab(installer, backup, _previews, _hub, _cache, _paths, _downloadBus, _host, updatesChecker);
        yield return new ModHubTab(_hub, _hofHirschfeld, _modhoster, _cache, installer,
            _previews, _settings, _downloadBus, _host);
        yield return new DownloadsTab(installer, _previews, _hub, _cache, _downloadBus, _host);
    }

    public Task ShutdownAsync()
    {
        _hub?.Dispose();
        _hofHirschfeld?.Dispose();
        _modhoster?.Dispose();
        _host?.Logger.Info("LS25 shutdown");
        return Task.CompletedTask;
    }

    // ---- IUpdateNotifier (Contracts v1.7.0) ----

    /// <summary>Zählt neue ModHub-Einträge seit dem letzten Katalog-Tab-Besuch
    /// pro aktiviertem Zielspiel. Baseline via Seen-Snapshot in
    /// <see cref="CatalogCache.SaveSeenSnapshot"/> — der wird beim Öffnen des
    /// ModHub-Tabs geschrieben und beim „Neuer Snapshot" gepatcht. Bis der
    /// User den Tab einmal besucht hat, gibt es keinen Badge (0 → gerendert
    /// wird kein Badge, weil der Host bei count=0 nichts zeigt).</summary>
    public Task<IReadOnlyList<GameUpdateInfo>> GetPendingUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_updateChecker is null || _installedUpdatesTracker is null || _activatedGames.Count == 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        const string language = "de";
        var catalogCount = _updateChecker.CountUnseen(language);
        var installedCount = _installedUpdatesTracker.PendingCount;
        var totalCount = catalogCount + installedCount;
        if (totalCount <= 0)
            return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(Array.Empty<GameUpdateInfo>());

        var parts = new List<string>(2);
        if (installedCount > 0)
            parts.Add(_installedUpdatesTracker.Summary is { Length: > 0 } s
                ? s
                : $"{installedCount} Mod-Update(s) verfügbar");
        if (catalogCount > 0)
            parts.Add($"{catalogCount} neue ModHub-Katalog-Einträge");
        var summary = string.Join(" · ", parts);
        var result = _activatedGames
            .Where(g => g.Target.SteamAppId is int)
            .Select(g => new GameUpdateInfo(g.Target.SteamAppId!.Value, totalCount, summary))
            .ToList();
        return Task.FromResult<IReadOnlyList<GameUpdateInfo>>(result);
    }

    private sealed class InstalledTab : IGameTabContribution
    {
        private readonly ModInstallService _installer;
        private readonly ModBackupService _backup;
        private readonly ModPreviewService _previews;
        private readonly ModHubService _hub;
        private readonly CatalogCache _cache;
        private readonly Ls25Paths _paths;
        private readonly DownloadEventBus _downloadBus;
        private readonly IHostServices _host;
        private readonly InstalledUpdatesChecker _updatesChecker;
        public InstalledTab(ModInstallService installer, ModBackupService backup,
            ModPreviewService previews, ModHubService hub, CatalogCache cache,
            Ls25Paths paths, DownloadEventBus downloadBus, IHostServices host,
            InstalledUpdatesChecker updatesChecker)
        { _installer = installer; _backup = backup; _previews = previews; _hub = hub; _cache = cache; _paths = paths; _downloadBus = downloadBus; _host = host; _updatesChecker = updatesChecker; }
        public string Id => "installed";
        public string Label => "Installiert";
        public string Icon => "\U0001F69C";
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView { DataContext = new InstalledModsViewModel(_installer, _backup, _previews, _hub, _cache, _paths, _downloadBus, _host, _updatesChecker) };
    }

    private sealed class ModHubTab : IGameTabContribution
    {
        private readonly ModHubService _hub;
        private readonly HofHirschfeldCatalogService _hof;
        private readonly ModhosterCatalogService _modhoster;
        private readonly CatalogCache _cache;
        private readonly ModInstallService _installer;
        private readonly ModPreviewService _previews;
        private readonly Ls25SettingsService _settings;
        private readonly DownloadEventBus _downloadBus;
        private readonly IHostServices _host;
        public ModHubTab(ModHubService hub, HofHirschfeldCatalogService hof,
            ModhosterCatalogService modhoster, CatalogCache cache,
            ModInstallService installer, ModPreviewService previews,
            Ls25SettingsService settings, DownloadEventBus downloadBus,
            IHostServices host)
        { _hub = hub; _hof = hof; _modhoster = modhoster; _cache = cache; _installer = installer; _previews = previews; _settings = settings; _downloadBus = downloadBus; _host = host; }
        public string Id => "modhub";
        public string Label => "ModHub";
        public string Icon => "\U0001F3EA"; // 🏪
        public int Order => 10;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new ModHubView { DataContext = new ModHubViewModel(_hub, _hof, _modhoster, _cache, _installer, _previews, _settings, _downloadBus, _host) };
    }

    private sealed class DownloadsTab : IGameTabContribution
    {
        private readonly ModInstallService _installer;
        private readonly ModPreviewService _previews;
        private readonly ModHubService _hub;
        private readonly CatalogCache _cache;
        private readonly DownloadEventBus _downloadBus;
        private readonly IHostServices _host;
        public DownloadsTab(ModInstallService installer, ModPreviewService previews,
            ModHubService hub, CatalogCache cache,
            DownloadEventBus downloadBus, IHostServices host)
        { _installer = installer; _previews = previews; _hub = hub; _cache = cache;
          _downloadBus = downloadBus; _host = host; }
        public string Id => "downloads";
        public string Label => "Downloads";
        public string Icon => "\U0001F4E5"; // 📥
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView { DataContext = new DownloadsViewModel(
                _installer, _previews, _hub, _cache, _downloadBus, _host) };
    }
}
