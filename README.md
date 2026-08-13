# KroModIx.Plugin.LS25

[![CI](https://github.com/KroModIx/KroModIx.Plugin.LS25/actions/workflows/ci.yml/badge.svg)](https://github.com/KroModIx/KroModIx.Plugin.LS25/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/KroModIx/KroModIx.Plugin.LS25)](https://github.com/KroModIx/KroModIx.Plugin.LS25/releases)

**Landwirtschafts-Simulator 25** als Plugin für den
[KroModIx](https://github.com/KroModIx/KroModIx). Aggregierter Katalog
über GIANTS ModHub + Hof Hirschfeld + modhoster, Preview-Bilder aus dem
Mod-ZIP, Update-Discovery für installierte Mods, KI-Zusammenfassung im
Detail-Dialog, Backup/Restore.

## Ziel-Spiel

**Farming Simulator 25** — Steam AppId 2300320.

- Windows: `Documents\My Games\FarmingSimulator2025\mods\`
- Linux (Proton): `<Proton-Prefix>/drive_c/users/steamuser/My Documents/My Games/FarmingSimulator2025/mods/`

## Neu in v1.15.0
- **DE+EN-Übersetzung** aller User-facing Strings (128 Keys) — Tab-Labels,
  Buttons, Placeholders, Tooltips, Statusmeldungen, Notifications, Dialoge.
  Sprachwechsel im Host schaltet nach Kachel-Reselect (Host-Tab-Cache
  invalidiert seit v1.14.7) live um.

## Neu in v1.14.0
- **DDS-Preview mit ffmpeg-Fallback**: Pfim decodiert BC1/BC2/BC3 und
  unkomprimierte Formate, für BC7 + exotische DXT-Varianten fällt der
  Converter jetzt auf `ffmpeg -f dds -i pipe:0 -f image2pipe -c:v png pipe:1`
  zurück. Kein Fehler wenn ffmpeg fehlt — Convert liefert dann null, die
  UI zeigt den Icon-Fallback.

## Features (v1.12.0)

### Installiert-Tab
- Alle Mods im Mods-Ordner mit Cover-Preview (aus modDesc.xml via
  DDS→PNG-Konvertierung), Autor, Version, Größe, Beschreibung
- Enable/Disable per Klick (rename → `.zip.disabled`)
- **Multi-Select** mit Bulk-Aktivieren/Deaktivieren/Deinstallieren
- **🔄 Updates prüfen** — vergleicht installierte Versionen mit dem
  ModHub-Katalog (Fuzzy-Match Filename → Titel)
- **⬆ Alle updaten** — installiert alle gefundenen Updates sequenziell
- **🔍 Details** per Doppelklick oder Button — öffnet GIANTS-Detail-Dialog
  mit Screenshots + KI-Zusammenfassung
- Drag&Drop von .zip-Files installiert direkt
- Backup + Restore aller Mods als ZIP mit Enabled-State-Manifest

### ModHub-Tab (Katalog)
- GIANTS-ModHub aggregiert mit Hof Hirschfeld und modhoster
- Suche, Kategorien, „nur neu"-Toggle
- Doppelklick öffnet Detail-Dialog mit Screenshots + Beschreibung + KI

### Downloads-Tab
- Alle heruntergeladenen .zip-Files mit Cover + Beschreibung
- **📥 Alle installieren** — Bulk-Install-Button
- Pro Row: Installieren + 🔍 Details + Löschen
- Auto-Refresh via FileSystemWatcher

### Einstellungen
- Katalog-Sprache, Katalog-Refresh-Intervall
- KI-Provider wird zentral im Host konfiguriert (`_host.Ai`)

### IUpdateNotifier
Grüner ↑-Badge auf der FS25-Kachel **nur bei echten Updates für deine
installierten Mods** (v1.13.1). Auto-Check läuft 20 s nach Plugin-Load
im Hintergrund. Neue ModHub-Katalog-Einträge sind ein Community-News-
Signal und werden bewusst nicht mehr im Actionable-Badge summiert.

## Installation

Aus dem [Release](https://github.com/KroModIx/KroModIx.Plugin.LS25/releases)
das ZIP entpacken nach:

- **Windows:** `%APPDATA%\KroModIx\plugins\kroste.ls25\`
- **Linux:**   `~/.config/KroModIx/plugins/kroste.ls25/`

Alternativ: 1-Klick-Install über die Install-Karte in der KroModIx-Sidebar.

## Kataloge

| Quelle | Format | Direct-Download | Notes |
|---|---|---|---|
| GIANTS ModHub | HTML-Scraping | ✅ | Rate-Limit-schonend, Cache 24h |
| Hof Hirschfeld | HTML-Scraping | ❌ (Consent-Overlay) | Detail-Link im Browser |
| modhoster | Public JSON | ❌ (Login-Pflicht) | Detail-Link im Browser |

## Entwicklung

```bash
dotnet build
```

Braucht Zugriff auf das KroModIx-GitHub-Packages-Feed für
`KroModIx.Plugin.Contracts`:

- CI: `secrets.GITHUB_TOKEN` reicht
- Lokal: `gh auth refresh -s read:packages` + `nuget.config` mit
  `packageSourceMapping` (siehe Repo)

Release: Tag `vX.Y.Z` setzen + pushen → GitHub-Action baut Bundle-ZIP.

## Lizenz

MIT — siehe [LICENSE](LICENSE).

---

☕ [buymeacoffee.com/kroste](https://buymeacoffee.com/kroste)
