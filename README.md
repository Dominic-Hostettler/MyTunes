# MyTunes

Kleiner, schneller Musik-Browser für einen `iTunes Media\Music`-Ordner (Google Drive / sonstige Sync-Ordner).
Eine einzige C#-Datei → `MyTunes.exe` mit `csc.exe` gebaut. WPF, keine Dependencies, kein Installer.

## Start

`MyTunes.cmd` doppelklicken — baut die exe bei Bedarf und startet sie.

## Features

- **Trackliste** wie iTunes: gruppiert nach Album, sortierbare/resizable Spalten
  - 9 Spalten ein-/ausblendbar (`Ansicht` oder Rechtsklick auf die Kopfzeile): `#`, `Titel`, `Interpret`, `Album`, `Dauer`, `Größe`, `Geändert`, `Erstellt`, `Dateiname`
  - Spaltenklick = sortieren auf/ab (Pfeil-Indikator), Album-Spalte = gruppierte Ansicht; Sortierkette: Spalte → Album → Titelnummer → Titel
  - Interpret/Album/Titel kommen aus der Ordnerstruktur, keine ID3-Tags nötig
  - Dauer via Shell32 → WMP-Fallback (Datei laden wie beim Abspielen) → im Cache gespeichert
- **Player-Leiste**: `⏮` Anfang/vorheriger (< 0,5 s im Track → vorheriger), `▶/⏸`, `⏭` — jeweils als große Drittel-Klickzone
  - selbst gezeichnete Seek- + Lautstärke-Leiste (Klick/Drag), Lautsprecher = Stumm, Doppelklick auf Titel = zum gespielten Track springen
- **Tasten**: `→`/`←` relativ zum spielenden Track, `Enter` fokussierter → selektierter → erster Track, `Leertaste` Pause (immer)
- **Kontextmenü** auf Track: Ausschneiden/Kopieren/Einfügen, Alles auswählen, Datei im Explorer anzeigen, Informationen (Popup mit allen Metadaten, kopierbar)
- **Drag & Drop**: Tracks in ein Explorer-Fenster ziehen = kopieren (nie verschieben)
- **Themes**: `Ansicht > Design` — Normal / Alchemist (violetter Akzent, fast schwarze Flächen), jeweils hell/dunkel; Auswahl + Dunkelmodus persistent
- **Custom-Scrollbar**: schmale eckige Pille, dragbar, Seitenwechsel per Spur-Klick
- **Cache**: `tracks.txt` (Pfad + Metadaten/Zeile) → Liste sofort beim Start; Hintergrund-Scan merged neue/gelöschte Tracks inkrementell
- **Persistenz**: `window.txt` (Bounds, Modus, Theme), `cols.txt` (Spalten-Sichtbarkeit/-Reihenfolge/-Breiten), `tracks.txt` (Cache)

## Menüs

| Menü | Einträge |
|------|----------|
| **Datei** | Medien neu scannen (`F5`), Beenden |
| **Bearbeiten** | Rückgängig/Wiederherstellen (`Strg+Z`/`Strg+Y`), Ausschneiden/Kopieren/Einfügen (`Strg+X/C/V`), Alles auswählen (`Strg+A`), Seite durchsuchen (`Strg+F`), Zum gespielten Track (`Strg+L`) |
| **Ansicht** | Dunkelmodus, Design (Normal/Alchemist), 9 Spalten-Toggles |

## Konfiguration

Musik-Pfad steht oben in `MyTunes.cs` (`const string Root`), aktuell:

```
G:\Meine Ablage\Synchronized\iTunes reduced Copy\iTunes Media\Music
```

- **Icons**: Font Awesome Free (`fonts/fa-solid-900.ttf`, SIL OFL), zur Laufzeit ohne Installation geladen; Menü-Icons werden als Bitmaps gerendert

## Hinweise

- Scan-Geschwindigkeit hängt am Dateisystem (Drive-Stream langsam, synchronisiert schnell).
- Wiedergabe über Windows Media Player (COM) — FLAC o.ä. nur wenn WMP es kann.
- Build-Befehl siehe `MyTunes.cmd` (.NET Framework 4.x, Teil von Windows).
