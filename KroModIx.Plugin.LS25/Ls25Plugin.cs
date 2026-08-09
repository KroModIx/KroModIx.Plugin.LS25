using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using KroModIx.Plugin.Contracts;
using KroModIx.Plugin.LS25.Services;
using KroModIx.Plugin.LS25.Views;

namespace KroModIx.Plugin.LS25;

public sealed class Ls25Plugin : IGameModPlugin
{
    public PluginMetadata Metadata { get; } = new(
        Id: "kroste.ls25",
        DisplayName: "Landwirtschafts-Simulator 25",
        Version: "1.0.0",
        Author: "Kroste",
        Description: "Mod-Manager für Farming Simulator 25 — Kroste-Card-Look. Per-Row-Buttons, Cover, INSTALLIERT- und ⭐ EMPFOHLEN-Badges, Spielstart via Steam, Mod-Updates, Detail-Dialog, aggregierter ModHub, Backup/Restore, KI-Zusammenfassung über zentralen Host-Provider (IHostServices.Ai).");

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
    private readonly Dictionary<string, ModInstallService> _installers = new();
    private readonly Dictionary<string, ModBackupService> _backups = new();
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
            host.Logger.Info("LS25 initialisiert: Mods-Ordner = {Path}", modsDir);
        }
        return Task.CompletedTask;
    }

    public IEnumerable<IGameTabContribution> GetTabContributions(DetectedGame game)
    {
        if (!_installers.TryGetValue(game.Target.GameId, out var installer) || _host is null
            || _hub is null || _cache is null || _hofHirschfeld is null || _modhoster is null
            || _paths is null || _settings is null || _previews is null || _downloadBus is null
            || !_backups.TryGetValue(game.Target.GameId, out var backup))
            yield break;

        yield return new InstalledTab(installer, backup, _previews, _hub, _cache, _paths, _downloadBus, _host);
        yield return new ModHubTab(_hub, _hofHirschfeld, _modhoster, _cache, installer,
            _previews, _settings, _downloadBus, _host);
        yield return new DownloadsTab(installer, _previews, _downloadBus, _host);
    }

    public Task ShutdownAsync()
    {
        _hub?.Dispose();
        _hofHirschfeld?.Dispose();
        _modhoster?.Dispose();
        _host?.Logger.Info("LS25 shutdown");
        return Task.CompletedTask;
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
        public InstalledTab(ModInstallService installer, ModBackupService backup,
            ModPreviewService previews, ModHubService hub, CatalogCache cache,
            Ls25Paths paths, DownloadEventBus downloadBus, IHostServices host)
        { _installer = installer; _backup = backup; _previews = previews; _hub = hub; _cache = cache; _paths = paths; _downloadBus = downloadBus; _host = host; }
        public string Id => "installed";
        public string Label => "Installiert";
        public string Icon => "\U0001F69C";
        public int Order => 0;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new InstalledModsView { DataContext = new InstalledModsViewModel(_installer, _backup, _previews, _hub, _cache, _paths, _downloadBus, _host) };
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
        private readonly DownloadEventBus _downloadBus;
        private readonly IHostServices _host;
        public DownloadsTab(ModInstallService installer, ModPreviewService previews,
            DownloadEventBus downloadBus, IHostServices host)
        { _installer = installer; _previews = previews; _downloadBus = downloadBus; _host = host; }
        public string Id => "downloads";
        public string Label => "Downloads";
        public string Icon => "\U0001F4E5"; // 📥
        public int Order => 20;
        public bool IsVisible(DetectedGame game) => true;
        public Control CreateView(DetectedGame game, IHostServices host) =>
            new DownloadsView { DataContext = new DownloadsViewModel(_installer, _previews, _downloadBus, _host) };
    }
}
