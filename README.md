# MyTunes

Kleiner Musik-Browser für einen `iTunes Media\Music`-Ordner (Google Drive / sonstige Sync-Ordner).
Eine einzige C#-Datei → `MyTunes.exe` mit `csc.exe` gebaut. Keine Dependencies, kein Installer.

## Start

`MyTunes.cmd` doppelklicken — baut die exe bei Bedarf und startet sie.

## Features

- **Trackliste** wie iTunes: gruppiert nach Album, Spalten `#`, `Titel`, `Interpret`, `Album`
  - Spaltenklick = sortieren (Album-Spalte = gruppierte Ansicht, andere = flach)
  - Interpret/Album/Titel kommen aus der Ordnerstruktur, keine ID3-Tags nötig
- **Player-Leiste**: `⏮` Anfang/vorheriger (< 0,5 s im Track → vorheriger), `▶/⏸`, `⏭`,
  Lautstärke, selbst gezeichnete Seek-Leiste (Klick/Drag = Sprung), Zeit-Anzeige
- **Tasten**: `→` nächster, `←` Anfang/vorheriger, `Enter` abspielen, `Leertaste` Pause
- **Drag & Drop**: Tracks in ein Explorer-Fenster ziehen = kopieren (nie verschieben)
- **Cache**: `tracks.txt` (ein Pfad/Zeile) → Liste sofort beim Start; Hintergrund-Scan
  merged neue/gelöschte Tracks inkrementell (kein Flackern, kein Rebuild)
- **Statusleiste** mit Track-Anzahl

## Menüs

| Menü | Einträge |
|------|----------|
| **Datei** | Medien neu scannen (`F5`), Beenden |
| **Bearbeiten** | Rückgängig/Wiederherstellen (Wiedergabeverlauf, `Strg+Z`/`Strg+Y`), Ausschneiden/Kopieren/Einfügen als Dateien (`Strg+X/C/V`), Alles auswählen (`Strg+A`), Seite durchsuchen (`Strg+F`) |
| **Ansicht** | Dunkelmodus (umschaltbar) |

## Konfiguration

Musik-Pfad steht oben in `MyTunes.cs` (`const string Root`), aktuell:

```
G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music
```

## Hinweise

- Scan-Geschwindigkeit hängt am Dateisystem (Drive-Stream langsam, synchronisiert schnell).
- Wiedergabe über Windows Media Player (COM) — FLAC o.ä. nur wenn WMP es kann.
- Build-Befehl siehe `MyTunes.cmd` (.NET Framework 4.x, Teil von Windows).
