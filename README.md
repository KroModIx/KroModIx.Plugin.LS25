# KroModIx.Plugin.LS25

[![CI](https://github.com/Kroste/KroModIx.Plugin.LS25/actions/workflows/ci.yml/badge.svg)](https://github.com/Kroste/KroModIx.Plugin.LS25/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/Kroste/KroModIx.Plugin.LS25)](https://github.com/Kroste/KroModIx.Plugin.LS25/releases)

LS25-Mod-Manager als Plugin für den [KroModIx](https://github.com/KroModIx/KroModIx).
Extraktion des LS-ModManager-Kerns (List/Install/Enable/Disable/Uninstall) —
Katalog-Anbindung an ModHub/Hof Hirschfeld/modhoster, KI-Zusammenfassungen und
Backup/Restore folgen in späteren Versionen.

## Ziel-Spiel

- **Farming Simulator 25** (Steam App-ID 2300320)
  - Windows: `Documents\My Games\FarmingSimulator2025\mods\`
  - Linux (Proton): `<Proton-Präfix>/drive_c/users/steamuser/My Documents/My Games/FarmingSimulator2025/mods/`

## Aktueller Umfang (v0.4.0)

Fünf Tabs:

- **Installiert**: Mods im Mods-Ordner listen, Aktiv/Inaktiv toggeln, ZIP
  installieren, deinstallieren, Ordner öffnen.
- **ModHub**: GIANTS-Katalog scrapen, Suche, Kategorien-Filter, Direct-Download.
- **Hof Hirschfeld**: Community-Umbauten aus hof-hirschfeld.de. Detail im Browser
  (Consent-Overlay auf der Site erlaubt keinen Direct-Download).
- **modhoster** *(neu in v0.4)*: modhoster.de über offiziellen JSON-Endpunkt.
  Paginierung, Titel/Autor/Kategorie-Filter. Detail im Browser
  (Login-Pflicht + robots.txt sperrt Download-Endpunkte).
- **Downloads**: heruntergeladene ZIPs listen, installieren oder löschen.
- v0.5 — Backup/Restore, KI-Zusammenfassungen (via IHostServices AI-Provider)
- v0.6 — Preview-Bilder (DDS → PNG via DdsToPngConverter)

## Installation

Aus dem [Release](https://github.com/Kroste/KroModIx.Plugin.LS25/releases) das
ZIP entpacken nach:

- **Windows:** `%APPDATA%\KroModIx\plugins\ls25\`
- **Linux:**   `~/.config/KroModIx/plugins/ls25/`

Beim nächsten App-Start erkennt der Host das Plugin, ab v0.4 von KroModIx
läuft die Installation live über die Sidebar-Karte („Plugin verfügbar → ⬇ Installieren").

## Entwicklung

```bash
dotnet build
```

Braucht Zugriff auf das Kroste-GitHub-Packages-Feed:

- Im CI: `secrets.GITHUB_TOKEN` reicht (siehe `.github/workflows/ci.yml`).
- Lokal: `gh auth refresh -s read:packages` und danach
  `dotnet nuget update source kroste-github --username kroste --password $(gh auth token) --store-password-in-clear-text`.

## Lizenz

MIT — siehe [LICENSE](LICENSE).
