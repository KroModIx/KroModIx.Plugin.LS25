using System.Collections.Generic;
using KroModIx.Plugin.Contracts;

namespace KroModIx.Plugin.LS25.Services;

/// <summary>Uebersetzungs-Tabelle fuer alle User-facing Strings im
/// LS25-Plugin. Sprachen: <c>de</c> (Fallback) + <c>en</c>.
///
/// <para>Nutzung: <c>Strings.Init(host.Localization)</c> beim Plugin-Init,
/// dann ueberall <c>Strings.T("key")</c>. Bei fehlendem Key wird der Key
/// selbst zurueckgegeben (macht Missing-Translations sofort sichtbar).</para>
///
/// <para><b>Kein Live-Refresh bei Sprachwechsel:</b> die Strings werden
/// zum View-Constructor-Zeitpunkt gelesen. Bei Sprachwechsel im Host muss
/// der User die FS25-Kachel neu waehlen (Host-Tab-Cache erzeugt dann
/// neue View-Instanzen mit den frischen Uebersetzungen) oder die App neu
/// starten. Vollreactive-Bindings waeren komplex und lohnen sich fuer den
/// seltenen Anwendungsfall nicht.</para></summary>
public static class Strings
{
    private static ILocalization? _loc;

    public static void Init(ILocalization loc) => _loc = loc;

    public static string T(string key)
    {
        var iso = _loc?.CurrentIso ?? "de";
        if (iso.StartsWith("en") && En.TryGetValue(key, out var en)) return en;
        if (De.TryGetValue(key, out var de)) return de;
        return key;
    }

    private static readonly Dictionary<string, string> De = new()
    {
        // Tab-Labels
        ["tab.installed"] = "Installiert",
        ["tab.modhub"] = "ModHub",
        ["tab.downloads"] = "Downloads",

        // ---- InstalledModsView: Toolbar ----
        ["btn.check_updates"] = "🔄  Updates prüfen",
        ["btn.update_all"] = "⬆  Alle updaten",
        ["tooltip.update_all"] = "Installiert alle Updates sequenziell. Erst 'Updates pruefen' klicken damit was zu tun ist.",
        ["btn.install_zip"] = "📁  ZIP installieren…",
        ["btn.import_folder"] = "📂  Ordner importieren…",
        ["tooltip.import_folder"] = "Waehlt einen Ordner (z.B. Downloads/LS25) — alle .zip darin werden nacheinander installiert.",
        ["btn.refresh"] = "↺  Aktualisieren",
        ["btn.toggle_bulk"] = "🔀  Aktiv/Inaktiv",
        ["btn.uninstall_selection"] = "🗑  Auswahl deinstallieren",
        ["btn.mods_folder"] = "📂  Mod-Ordner",
        ["btn.backup"] = "💾  Backup",
        ["btn.restore"] = "♻  Restore",

        // ---- InstalledModsView: Filter-Zeile ----
        ["placeholder.filter_installed"] = "Installierte Mods filtern (Titel/Autor/Dateiname) …",
        ["btn.only_with_update"] = "⬆  Nur mit Update",
        ["label.mods_dir_prefix"] = "Mods-Ordner: {0}",

        // ---- InstalledModsView: Row-Buttons + Context-Menu ----
        ["btn.row_update"] = "⬆  Update",
        ["btn.row_details"] = "🔍  Details",
        ["tooltip.row_details_installed"] = "GIANTS-Detail-Dialog öffnen (Fuzzy-Match Filename → Katalog — Katalog muss vorher via ModHub-Tab geladen sein)",
        ["btn.row_toggle"] = "⏻  (De-)Aktivieren",
        ["btn.row_uninstall"] = "🗑  Deinstallieren",
        ["row.badge_enabled"] = "aktiv",
        ["row.badge_disabled"] = "inaktiv",
        ["row.badge_update_prefix"] = "⬆ Update v{0}",

        // ---- InstalledModsViewModel: Status/Notify ----
        ["status.reading_mods"] = "Mod-Liste wird gelesen …",
        ["status.no_mods"] = "Keine Mods im Mods-Ordner.",
        ["status.mods_summary"] = "{0} aktiv / {1} total · {2}",
        ["status.mods_read_error"] = "Fehler beim Lesen des Mods-Ordners: {0}",
        ["label.selected_count"] = "{0} ausgewählt",
        ["notify.mod_enabled"] = "Mod aktiviert: {0}",
        ["notify.mod_disabled"] = "Mod deaktiviert: {0}",
        ["notify.error_prefix"] = "Fehler: {0}",
        ["dialog.uninstall_bulk_title"] = "Mods deinstallieren",
        ["dialog.uninstall_bulk_msg"] = "{0} Mod(s) wirklich löschen?\n\n{1}",
        ["dialog.uninstall_bulk_more"] = "\n… und {0} weitere",
        ["dialog.btn_delete"] = "Löschen",
        ["dialog.btn_cancel"] = "Abbrechen",
        ["notify.bulk_toggle_enabled"] = "{0} Mod(s) aktiviert.",
        ["notify.bulk_toggle_disabled"] = "{0} Mod(s) deaktiviert.",
        ["notify.bulk_uninstalled"] = "{0} Mod(s) deinstalliert.",
        ["dialog.uninstall_single_title"] = "Mod deinstallieren",
        ["dialog.uninstall_single_msg"] = "„{0}“ wirklich löschen?",
        ["notify.uninstalled_prefix"] = "Deinstalliert: {0}",
        ["dialog.pick_zip_title"] = "Mod-ZIP wählen",
        ["dialog.pick_zip_filter"] = "LS25-Mod (.zip)",
        ["notify.installed_prefix"] = "Installiert: {0}",
        ["dialog.pick_folder_title"] = "Ordner mit LS25-ZIPs waehlen",
        ["notify.no_zips_in_folder"] = "Keine .zip-Dateien im Ordner gefunden.",
        ["dialog.bulk_import_title"] = "Bulk-Import",
        ["dialog.bulk_import_msg"] = "{0} ZIP-Datei(en) werden nacheinander in den Mods-Ordner installiert. Fortfahren?",
        ["dialog.btn_install"] = "Installieren",
        ["progress.bulk_import"] = "Bulk-Import: {0} Mods",
        ["progress.bulk_import_row"] = "{0}/{1}: {2}",
        ["notify.bulk_import_result"] = "Bulk-Import: {0} installiert, {1} Fehler.",
        ["notify.no_catalog_cache"] = "Kein Katalog-Cache vorhanden. Erst ModHub-Tab öffnen, damit der Katalog geladen wird.",
        ["notify.no_catalog_match"] = "Kein Katalog-Eintrag für „{0}\" gefunden (Fuzzy-Match hat nicht gegriffen).",
        ["notify.no_mod_id"] = "Katalog-Eintrag hat keine mod_id: {0}",
        ["notify.installed_drop_prefix"] = "Installiert (Drop): {0}",
        ["notify.drop_install_failed"] = "Drop-Install fehlgeschlagen ({0}): {1}",
        ["notify.updates_found"] = "Updates gefunden: {0} Mod(s).",
        ["notify.no_updates"] = "Keine Updates.",
        ["notify.update_check_error"] = "Update-Prüfung: {0}",
        ["notify.no_pending_updates"] = "Keine offenen Updates. Erst 🔄 Updates prüfen klicken.",
        ["progress.updates_running"] = "{0} LS25-Updates …",
        ["progress.update_row"] = "Update {0}/{1}: {2}",
        ["notify.updates_installed"] = "{0} Mod-Update(s) installiert.",
        ["notify.updates_partial"] = "{0} installiert, {1} Fehler.",
        ["notify.catalog_entry_missing"] = "Katalog-Eintrag für Update nicht mehr gefunden.",
        ["progress.update_prefix"] = "Update: {0}",
        ["notify.update_installed"] = "Update installiert: {0} → v{1}",
        ["notify.update_install_error"] = "Update-Fehler: {0}",
        ["notify.no_mods_to_backup"] = "Keine Mods vorhanden — nichts zu sichern.",
        ["progress.backup_running"] = "Backup erstellen …",
        ["progress.backup_row"] = "{0}/{1} · {2}",
        ["notify.backup_ok"] = "Backup: {0} Mods · {1} → {2}",
        ["notify.backup_error"] = "Backup-Fehler: {0}",
        ["dialog.pick_backup_title"] = "Backup-ZIP wählen",
        ["dialog.pick_backup_filter"] = "LS25-Backup (.zip)",
        ["notify.backup_invalid"] = "Backup ungültig: {0}",
        ["dialog.restore_title"] = "Backup wiederherstellen",
        ["dialog.restore_msg"] = "Backup vom {0} · {1} Mods.\nVorhandene Mod-ZIPs mit gleichem Namen werden überschrieben.\nFortfahren?",
        ["dialog.btn_restore"] = "Wiederherstellen",
        ["progress.restore_running"] = "Backup wiederherstellen …",
        ["notify.restore_ok"] = "Restore: {0} wiederhergestellt, {1} übersprungen.",
        ["notify.restore_error"] = "Restore-Fehler: {0}",

        // ---- ModHubView: Toolbar ----
        ["placeholder.all_sources"] = "Alle Quellen",
        ["placeholder.all_categories"] = "Alle Kategorien",
        ["placeholder.search_modhub"] = "Titel/Autor/Kategorie …",
        ["btn.reload_catalog"] = "↺  Katalog neu laden",
        ["label.sort"] = "Sortierung:",
        ["hint.modhub_sources"] = "GIANTS: In-App-Download · Hof Hirschfeld & modhoster: Detail-Klick öffnet die Seite im Browser (Consent-Overlay bzw. Login-Pflicht).",
        ["label.ai_summary"] = "🤖  KI-Zusammenfassung",
        ["btn.close_summary"] = "✕",

        // ---- ModHubView: Row-Badges + Buttons ----
        ["badge.installed"] = "✓ INSTALLIERT",
        ["badge.featured"] = "⭐ EMPFOHLEN",
        ["badge.new"] = "NEU",
        ["btn.row_download"] = "⬇  Herunterladen",
        ["btn.row_browser"] = "🌐  Im Browser",
        ["btn.row_show_details"] = "👁  Details",

        // ---- ModHubViewModel: Filter-Optionen ----
        ["filter.all_categories"] = "Alle Kategorien",
        ["filter.all_sources"] = "Alle Quellen",
        ["source.giants"] = "GIANTS ModHub",
        ["source.hof_hirschfeld"] = "Hof Hirschfeld",
        ["source.modhoster"] = "modhoster",
        ["sort.default"] = "Standard",
        ["sort.new_first"] = "NEU zuerst",
        ["sort.name"] = "Name (A–Z)",
        ["sort.author"] = "Autor (A–Z)",
        ["sort.category"] = "Kategorie (A–Z)",

        // ---- ModHubViewModel: Status/Notify ----
        ["status.loading_catalog"] = "Katalog wird geladen …",
        ["status.cache_summary"] = "{0} Mods aus Cache (Alter: {1} h).",
        ["status.cache_batch"] = "Cache: {0}/{1} Mods …",
        ["status.catalog_full_load"] = "Katalog-Load …",
        ["status.catalog_total"] = "{0} Mods im Katalog · {1} sichtbar",
        ["status.catalog_load_error"] = "Fehler beim Laden: {0}",
        ["status.giants_page"] = "GIANTS Seite {0} · {1} sichtbar",
        ["notify.no_mod_id_from_url"] = "Keine Mod-ID aus URL erkennbar: {0}",
        ["progress.download_prefix"] = "Download: {0}",
        ["notify.download_failed"] = "Download fehlgeschlagen (siehe Log).",
        ["notify.downloaded_prefix"] = "Heruntergeladen: {0}",
        ["notify.ai_unavailable"] = "KI-Provider nicht erreichbar — bitte in den KroModIx-Einstellungen konfigurieren.",
        ["status.summary_loading"] = "Lade Detail-Beschreibung für \"{0}\" …",
        ["status.summary_no_desc"] = "Keine Beschreibung im Detail-Endpoint gefunden.",
        ["status.summary_building"] = "KI-Zusammenfassung wird erstellt via {0} …",
        ["status.summary_no_answer"] = "KI hat keine Antwort geliefert.",

        // ---- DownloadsView: Toolbar ----
        ["btn.install_all"] = "📥  Alle installieren",
        ["tooltip.install_all"] = "Installiert alle Downloads (überschreibt bestehende Versionen). Ideal nach einem Update-Batch.",
        ["btn.downloads_folder"] = "📂  Downloads-Ordner",
        ["label.downloads_prefix"] = "Downloads: {0}",

        // ---- DownloadsView: Row ----
        ["btn.row_install"] = "📥  Installieren",
        ["tooltip.row_show_detail_download"] = "ModHub-Detail-Dialog öffnen (Fuzzy-Match auf Katalog — braucht geladenen ModHub-Katalog)",
        ["btn.row_delete"] = "🗑  Löschen",

        // ---- DownloadsViewModel ----
        ["status.reading_downloads"] = "Downloads werden gelesen …",
        ["status.downloads_dir_missing"] = "(nicht konfiguriert)",
        ["status.no_downloads"] = "Keine heruntergeladenen Mods.",
        ["status.downloads_summary"] = "{0} ZIPs · {1:F1} MB gesamt",
        ["status.downloads_read_error"] = "Fehler beim Lesen des Downloads-Ordners: {0}",
        ["notify.downloads_updated"] = "Downloads aktualisiert: {0}",
        ["notify.no_downloads_install"] = "Keine Downloads zu installieren.",
        ["progress.install_downloads"] = "Installiere {0} Downloads …",
        ["progress.install_row"] = "Installiere {0}/{1}: {2}",
        ["notify.downloads_installed"] = "{0} Downloads installiert.",
        ["notify.downloads_install_partial"] = "{0} installiert, {1} Fehler (siehe Log).",
        ["dialog.delete_download_title"] = "Download löschen",
        ["dialog.delete_download_msg"] = "„{0}“ aus dem Downloads-Ordner löschen?",
        ["notify.deleted_prefix"] = "Gelöscht: {0}",

        // ---- ModDetailWindow ----
        ["btn.detail_browser"] = "🌐  Detail im Browser",
        ["btn.detail_download"] = "⬇  Download",
        ["btn.detail_summarize"] = "🤖  Zusammenfassen (Ollama)",
        ["detail.section.screenshots"] = "Screenshots",
        ["detail.section.description"] = "Beschreibung",
        ["detail.section.ai_summary"] = "🤖 KI-Zusammenfassung",

        // ---- ModDetailViewModel ----
        ["detail.status.loading"] = "Detail-Seite wird geladen …",
        ["detail.status.load_error"] = "Detail-Seite konnte nicht geladen werden.",
        ["detail.status.short_error"] = "Fehler beim Laden.",
        ["detail.no_description"] = "Keine Beschreibung im Detail-Endpoint.",
        ["detail.status.summary"] = "{0} Screenshot(s) · v{1}",
        ["detail.error_prefix"] = "Fehler: {0}",
        ["notify.detail_wait"] = "Bitte warten bis Detail geladen ist.",
        ["detail.summary.busy"] = "KI-Zusammenfassung via {0} …",
        ["detail.summary.no_answer"] = "KI hat keine Antwort geliefert.",
    };

    private static readonly Dictionary<string, string> En = new()
    {
        // Tab-Labels
        ["tab.installed"] = "Installed",
        ["tab.modhub"] = "ModHub",
        ["tab.downloads"] = "Downloads",

        // ---- InstalledModsView: Toolbar ----
        ["btn.check_updates"] = "🔄  Check for updates",
        ["btn.update_all"] = "⬆  Update all",
        ["tooltip.update_all"] = "Installs all updates sequentially. Click 'Check for updates' first so there is something to do.",
        ["btn.install_zip"] = "📁  Install ZIP…",
        ["btn.import_folder"] = "📂  Import folder…",
        ["tooltip.import_folder"] = "Pick a folder (e.g. Downloads/LS25) — every .zip in it is installed one after another.",
        ["btn.refresh"] = "↺  Refresh",
        ["btn.toggle_bulk"] = "🔀  Enable/Disable",
        ["btn.uninstall_selection"] = "🗑  Uninstall selection",
        ["btn.mods_folder"] = "📂  Mods folder",
        ["btn.backup"] = "💾  Backup",
        ["btn.restore"] = "♻  Restore",

        // ---- InstalledModsView: Filter-Zeile ----
        ["placeholder.filter_installed"] = "Filter installed mods (title/author/filename) …",
        ["btn.only_with_update"] = "⬆  Only with update",
        ["label.mods_dir_prefix"] = "Mods folder: {0}",

        // ---- InstalledModsView: Row-Buttons + Context-Menu ----
        ["btn.row_update"] = "⬆  Update",
        ["btn.row_details"] = "🔍  Details",
        ["tooltip.row_details_installed"] = "Open GIANTS detail dialog (fuzzy match filename → catalog — the catalog has to be loaded via ModHub tab first)",
        ["btn.row_toggle"] = "⏻  Enable/Disable",
        ["btn.row_uninstall"] = "🗑  Uninstall",
        ["row.badge_enabled"] = "active",
        ["row.badge_disabled"] = "inactive",
        ["row.badge_update_prefix"] = "⬆ update v{0}",

        // ---- InstalledModsViewModel: Status/Notify ----
        ["status.reading_mods"] = "Reading mod list …",
        ["status.no_mods"] = "No mods in the mods folder.",
        ["status.mods_summary"] = "{0} active / {1} total · {2}",
        ["status.mods_read_error"] = "Error reading mods folder: {0}",
        ["label.selected_count"] = "{0} selected",
        ["notify.mod_enabled"] = "Mod enabled: {0}",
        ["notify.mod_disabled"] = "Mod disabled: {0}",
        ["notify.error_prefix"] = "Error: {0}",
        ["dialog.uninstall_bulk_title"] = "Uninstall mods",
        ["dialog.uninstall_bulk_msg"] = "Really delete {0} mod(s)?\n\n{1}",
        ["dialog.uninstall_bulk_more"] = "\n… and {0} more",
        ["dialog.btn_delete"] = "Delete",
        ["dialog.btn_cancel"] = "Cancel",
        ["notify.bulk_toggle_enabled"] = "{0} mod(s) enabled.",
        ["notify.bulk_toggle_disabled"] = "{0} mod(s) disabled.",
        ["notify.bulk_uninstalled"] = "{0} mod(s) uninstalled.",
        ["dialog.uninstall_single_title"] = "Uninstall mod",
        ["dialog.uninstall_single_msg"] = "Really delete „{0}“?",
        ["notify.uninstalled_prefix"] = "Uninstalled: {0}",
        ["dialog.pick_zip_title"] = "Pick mod ZIP",
        ["dialog.pick_zip_filter"] = "LS25 mod (.zip)",
        ["notify.installed_prefix"] = "Installed: {0}",
        ["dialog.pick_folder_title"] = "Pick folder with LS25 ZIPs",
        ["notify.no_zips_in_folder"] = "No .zip files found in the folder.",
        ["dialog.bulk_import_title"] = "Bulk import",
        ["dialog.bulk_import_msg"] = "{0} ZIP file(s) will be installed one after another into the mods folder. Continue?",
        ["dialog.btn_install"] = "Install",
        ["progress.bulk_import"] = "Bulk import: {0} mods",
        ["progress.bulk_import_row"] = "{0}/{1}: {2}",
        ["notify.bulk_import_result"] = "Bulk import: {0} installed, {1} error(s).",
        ["notify.no_catalog_cache"] = "No catalog cache available. Open the ModHub tab first so the catalog gets loaded.",
        ["notify.no_catalog_match"] = "No catalog entry found for „{0}“ (fuzzy match did not hit).",
        ["notify.no_mod_id"] = "Catalog entry has no mod_id: {0}",
        ["notify.installed_drop_prefix"] = "Installed (drop): {0}",
        ["notify.drop_install_failed"] = "Drop install failed ({0}): {1}",
        ["notify.updates_found"] = "Updates found: {0} mod(s).",
        ["notify.no_updates"] = "No updates.",
        ["notify.update_check_error"] = "Update check: {0}",
        ["notify.no_pending_updates"] = "No pending updates. Click 🔄 Check for updates first.",
        ["progress.updates_running"] = "{0} LS25 updates …",
        ["progress.update_row"] = "Update {0}/{1}: {2}",
        ["notify.updates_installed"] = "{0} mod update(s) installed.",
        ["notify.updates_partial"] = "{0} installed, {1} error(s).",
        ["notify.catalog_entry_missing"] = "Catalog entry for update no longer found.",
        ["progress.update_prefix"] = "Update: {0}",
        ["notify.update_installed"] = "Update installed: {0} → v{1}",
        ["notify.update_install_error"] = "Update error: {0}",
        ["notify.no_mods_to_backup"] = "No mods present — nothing to back up.",
        ["progress.backup_running"] = "Creating backup …",
        ["progress.backup_row"] = "{0}/{1} · {2}",
        ["notify.backup_ok"] = "Backup: {0} mods · {1} → {2}",
        ["notify.backup_error"] = "Backup error: {0}",
        ["dialog.pick_backup_title"] = "Pick backup ZIP",
        ["dialog.pick_backup_filter"] = "LS25 backup (.zip)",
        ["notify.backup_invalid"] = "Backup invalid: {0}",
        ["dialog.restore_title"] = "Restore backup",
        ["dialog.restore_msg"] = "Backup from {0} · {1} mods.\nExisting mod ZIPs with the same name will be overwritten.\nContinue?",
        ["dialog.btn_restore"] = "Restore",
        ["progress.restore_running"] = "Restoring backup …",
        ["notify.restore_ok"] = "Restore: {0} restored, {1} skipped.",
        ["notify.restore_error"] = "Restore error: {0}",

        // ---- ModHubView: Toolbar ----
        ["placeholder.all_sources"] = "All sources",
        ["placeholder.all_categories"] = "All categories",
        ["placeholder.search_modhub"] = "Title/author/category …",
        ["btn.reload_catalog"] = "↺  Reload catalog",
        ["label.sort"] = "Sort:",
        ["hint.modhub_sources"] = "GIANTS: in-app download · Hof Hirschfeld & modhoster: click details to open the page in the browser (consent overlay / login required).",
        ["label.ai_summary"] = "🤖  AI summary",
        ["btn.close_summary"] = "✕",

        // ---- ModHubView: Row-Badges + Buttons ----
        ["badge.installed"] = "✓ INSTALLED",
        ["badge.featured"] = "⭐ FEATURED",
        ["badge.new"] = "NEW",
        ["btn.row_download"] = "⬇  Download",
        ["btn.row_browser"] = "🌐  In browser",
        ["btn.row_show_details"] = "👁  Details",

        // ---- ModHubViewModel: Filter-Optionen ----
        ["filter.all_categories"] = "All categories",
        ["filter.all_sources"] = "All sources",
        ["source.giants"] = "GIANTS ModHub",
        ["source.hof_hirschfeld"] = "Hof Hirschfeld",
        ["source.modhoster"] = "modhoster",
        ["sort.default"] = "Default",
        ["sort.new_first"] = "NEW first",
        ["sort.name"] = "Name (A–Z)",
        ["sort.author"] = "Author (A–Z)",
        ["sort.category"] = "Category (A–Z)",

        // ---- ModHubViewModel: Status/Notify ----
        ["status.loading_catalog"] = "Loading catalog …",
        ["status.cache_summary"] = "{0} mods from cache (age: {1} h).",
        ["status.cache_batch"] = "Cache: {0}/{1} mods …",
        ["status.catalog_full_load"] = "Catalog load …",
        ["status.catalog_total"] = "{0} mods in catalog · {1} visible",
        ["status.catalog_load_error"] = "Load error: {0}",
        ["status.giants_page"] = "GIANTS page {0} · {1} visible",
        ["notify.no_mod_id_from_url"] = "No mod id recognizable in URL: {0}",
        ["progress.download_prefix"] = "Download: {0}",
        ["notify.download_failed"] = "Download failed (see log).",
        ["notify.downloaded_prefix"] = "Downloaded: {0}",
        ["notify.ai_unavailable"] = "AI provider not reachable — configure it in KroModIx settings.",
        ["status.summary_loading"] = "Loading detail description for \"{0}\" …",
        ["status.summary_no_desc"] = "No description found in detail endpoint.",
        ["status.summary_building"] = "Building AI summary via {0} …",
        ["status.summary_no_answer"] = "AI returned no answer.",

        // ---- DownloadsView: Toolbar ----
        ["btn.install_all"] = "📥  Install all",
        ["tooltip.install_all"] = "Installs all downloads (overwrites existing versions). Ideal after an update batch.",
        ["btn.downloads_folder"] = "📂  Downloads folder",
        ["label.downloads_prefix"] = "Downloads: {0}",

        // ---- DownloadsView: Row ----
        ["btn.row_install"] = "📥  Install",
        ["tooltip.row_show_detail_download"] = "Open ModHub detail dialog (fuzzy match against catalog — needs loaded ModHub catalog)",
        ["btn.row_delete"] = "🗑  Delete",

        // ---- DownloadsViewModel ----
        ["status.reading_downloads"] = "Reading downloads …",
        ["status.downloads_dir_missing"] = "(not configured)",
        ["status.no_downloads"] = "No downloaded mods.",
        ["status.downloads_summary"] = "{0} ZIPs · {1:F1} MB total",
        ["status.downloads_read_error"] = "Error reading downloads folder: {0}",
        ["notify.downloads_updated"] = "Downloads updated: {0}",
        ["notify.no_downloads_install"] = "No downloads to install.",
        ["progress.install_downloads"] = "Installing {0} downloads …",
        ["progress.install_row"] = "Installing {0}/{1}: {2}",
        ["notify.downloads_installed"] = "{0} downloads installed.",
        ["notify.downloads_install_partial"] = "{0} installed, {1} error(s) (see log).",
        ["dialog.delete_download_title"] = "Delete download",
        ["dialog.delete_download_msg"] = "Delete „{0}“ from the downloads folder?",
        ["notify.deleted_prefix"] = "Deleted: {0}",

        // ---- ModDetailWindow ----
        ["btn.detail_browser"] = "🌐  Details in browser",
        ["btn.detail_download"] = "⬇  Download",
        ["btn.detail_summarize"] = "🤖  Summarize (Ollama)",
        ["detail.section.screenshots"] = "Screenshots",
        ["detail.section.description"] = "Description",
        ["detail.section.ai_summary"] = "🤖 AI summary",

        // ---- ModDetailViewModel ----
        ["detail.status.loading"] = "Loading detail page …",
        ["detail.status.load_error"] = "Detail page could not be loaded.",
        ["detail.status.short_error"] = "Load error.",
        ["detail.no_description"] = "No description in detail endpoint.",
        ["detail.status.summary"] = "{0} screenshot(s) · v{1}",
        ["detail.error_prefix"] = "Error: {0}",
        ["notify.detail_wait"] = "Please wait until details are loaded.",
        ["detail.summary.busy"] = "AI summary via {0} …",
        ["detail.summary.no_answer"] = "AI returned no answer.",
    };
}
