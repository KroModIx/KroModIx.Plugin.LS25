# KroModIx.Plugin.LS25

## Grundlagen

- **Was:** LS25-Mod-Manager als Plugin für KroModIx. Extraktion des
  LS-ModManager-Kerns; Zielspiel: FS25 (Steam App-ID 2300320).
- **Stack:** .NET 10, `KroModIx.Plugin.Contracts` als PackageReference
  aus GitHub Packages, Avalonia (Compile-Zeit, kommt vom Host mit).
- **Repo:** `github.com/Kroste/KroModIx.Plugin.LS25`
- **Deploy-Ziel:** `~/.config/KroModIx/plugins/ls25/` (Linux) bzw.
  `%APPDATA%\KroModIx\plugins\ls25\` (Windows).

## Aktueller Stand

**v0.4.0 (M3.4 — modhoster-Katalog):**
- Fünfter Tab **modhoster** — nutzt den offiziellen JSON-Endpunkt
  `/mods.json?game_id=1` (game_id=1 ist LS25) statt HTML-Scrape.
- **Kein In-App-Download**: Login-Pflicht + robots.txt sperrt Download-
  Endpunkte. Detail-Klick öffnet die Browser-Seite.
- `ModhosterCatalogService` (190 Z., aus LS-ModManager mit HttpClient-
  Injection adaptiert).
- `ModhosterViewModel` + `ModhosterView`: Paginierung (statt Kategorien
  wie bei Hof Hirschfeld), Live-Suche in Titel/Autor/Kategorie,
  „Katalog neu laden", „Detail im Browser".

**v0.3.0 (M3.3 — Hof-Hirschfeld-Katalog):**
- Vierter Tab **Hof Hirschfeld** (Community-Umbauten für LS25). Iteriert
  über Kategorien-Slugs von der Startseite, parst Mod-Karten pro Kategorie.
- **Kein In-App-Download**: die Site hat ein Consent-Overlay für
  Werbung/Downloads, das über HTTP-Scrape nicht umgehbar wäre (und wollen
  wir auch nicht — die Community-Site ist werbefinanziert). Detail-Button
  öffnet die Mod-Seite im Browser.
- `HofHirschfeldCatalogService` (180 Z., 1:1 aus LS-ModManager mit
  HttpClient-Injection statt globaler Instanz).
- `HofHirschfeldViewModel` + `HofHirschfeldView`: Live-Suche in
  Titel/Kategorie, „Katalog neu laden"-Button, Detail-Button, Hinweis-Text
  zum Consent-Overlay.

**v0.2.0 (M3.2 — ModHub-Katalog):**
- Drei Tabs: „Installiert" (v0.1), „ModHub" (neu), „Downloads" (neu)
- `ModHubService` (658 Zeilen aus LS-ModManager extrahiert): HTTPS-Scraping
  von farming-simulator.com, Katalog-Seiten iterativ laden, Kategorien +
  Detail-Fetch, Direct-Download vom GIANTS-CDN.
- `CatalogCache`: JSON-Cache im PluginCacheDir/catalog/ mit atomarem
  Save + Sidecar-Seen-Snapshot für "NEU seit letztem Start"-Badge.
- `DdsToPngConverter` (Pfim + SkiaSharp): DDS-Icons in Mods → PNG für
  spätere Preview-Anzeige (in v0.6 im Installiert-Tab genutzt).
- `Ls25Paths`: Plugin-lokaler Pfad-Helper (statt globaler AppPaths in
  LS-ModManager); nutzt `IHostServices.PluginCacheDir` für Downloads,
  Previews, Katalog.
- `ModHubViewModel` + `ModHubView`: Katalog-Tab mit Live-Suche
  (Titel/Autor), Kategorie-Dropdown, Refresh-Button, Detail-im-Browser,
  Download-Klick (Progress via IHostServices.BeginProgress).
- `DownloadsViewModel` + `DownloadsView`: Downloads-Tab listet ZIPs im
  Plugin-Downloads-Ordner, Install-Klick reicht sie an `ModInstallService`
  weiter, Delete-Klick löscht.
- `ModInstallService` erweitert: `ListDownloaded()`, `DeleteDownload()`.

**v0.1.0 (M3.1 — Skelett + Installiert-Tab):**
- `Ls25Plugin`: Entry-Point, ein Target (FS25/2300320).
- `Ls25PathResolver`: Findet Mods-Ordner via Host-liefertem `DetectedGame.UserDataDir`
  (Proton-Docs unter Linux) bzw. Windows Documents/My Games/FarmingSimulator2025.
- `ModDescReader`: liest modDesc.xml aus einer ZIP (Metadaten, DE/EN-Localized
  Title und Description, Author, Version, Multiplayer-Flag). Übernommen aus LS-ModManager.
  Preview-Extraction (DDS → PNG) fehlt noch (v0.6).
- `ModInstallService`: List (aktive + `.zip.disabled` inaktive), Install (mit
  modDesc.xml-Validierung), Uninstall, SetEnabled (.zip.disabled-Toggle).
- `InstalledModsView` + `InstalledModsViewModel`: Code-only Avalonia-View
  (kein XAML im Plugin — vermeidet Compiled-Bindings-Duplizierung). Toolbar:
  Install-ZIP, Refresh, Mods-Ordner öffnen, Toggle Enable/Disable, Uninstall
  (mit Confirm-Dialog). Zeigt Titel/Author/Version/Größe/State pro Mod.

## Roadmap

- **v0.5** — Backup/Restore, KI-Zusammenfassungen im Mod-Detail-Dialog (via
  IHostServices AI-Provider wenn im Host verfügbar).
- **v0.6** — Preview-Bilder in der Installiert- und ModHub-Liste (DDS → PNG
  via bereits vorhandenem DdsToPngConverter + Cover-Cache vom ModHub).

## Referenz

- **Kein XAML im Plugin**: Views sind Code-Behind-only (Avalonia-XAML im Plugin
  würde einen doppelten Compiled-Bindings-Loader im Prozess bedeuten und
  Style-Resource-Casts brechen; sehr fragil in isolierten AssemblyLoadContexts —
  wir haben zwar keinen LoadContext, aber Code-only ist trotzdem stabiler).
- **NuGet-Feed:** `nuget.config` bindet `kroste-github` (GitHub Packages) ein.
  Lokal braucht der Entwickler-PAT mit `read:packages`-Scope. CI nutzt `GITHUB_TOKEN`.
- **Plugin-Bundle:** Release-Workflow packt `KroModIx.Plugin.LS25.dll` +
  `plugin.json` als `KroModIx.Plugin.LS25-X.Y.Z.zip`.
- **LS-ModManager-Standalone**: bleibt existieren, wird nicht angetastet. Ab v0.1
  landen neue LS25-Features hier im Plugin, nicht mehr im standalone LS-ModManager.
