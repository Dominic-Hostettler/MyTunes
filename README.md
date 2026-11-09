# MyTunes

Kleiner, schneller Musik-Browser für **beliebige Ordner mit Musikdateien** — kein iTunes, kein Sync-Dienst nötig.
Eine einzige C#-Datei → `MyTunes.exe` mit `csc.exe` gebaut. WPF, keine Dependencies, kein Installer.

## Start

`MyTunes.cmd` doppelklicken — baut die exe bei Bedarf und startet sie.

Musik-Ordner wählen: `Datei > Quellordner wählen...` — wird in `root.txt` gemerkt und beim nächsten Start wiederverwendet.

## Features

- **Trackliste** wie iTunes: gruppiert nach Album (oder Interpret), sortierbare/resizable Spalten
  - 11 Spalten ein-/ausblendbar (`Ansicht > Spalten` oder Rechtsklick auf die Kopfzeile): `#`, `Titel`, `Interpret`, `Album`, `Dauer`, `Größe`, `Geändert`, `Erstellt`, `Dateiname`, `Jahr`, `Genre`
  - Sichtbarkeit, Reihenfolge und Breiten bleiben über Restarts erhalten (`cols.txt`)
  - Spaltenklick = sortieren auf/ab (Pfeil-Indikator); Sortierkette: Spalte → Album → Titelnummer → Titel
  - **Metadaten aus echten ID3-Tags** (wie Explorer > Details): Interpret/Album/Titel/Nummer + Jahr/Genre — via Shell32, mit `ExtendedProperty` und WMP-Fallback für Sync-Ordner (4 parallele Threads, Fortschritt oben rechts)
  - Dauer via Shell32 → WMP-Fallback → im Cache gespeichert
  - Buchstabensuche in der Liste (Tastatur, mehrere Buchstaben), Klick auf Gruppenzeile selektiert Gruppe
- **Player-Leiste**: `⏮` Anfang/vorheriger (< 0,5 s im Track → vorheriger), `▶/⏸`, `⏭` — jeweils große Klickzone
  - selbst gezeichnete Seek- + Lautstärke-Leiste (Klick/Drag), Lautsprecher = Stumm
  - Titelanzeige: **Titel** fett – Interpret – Album; Doppelklick = zum gespielten Track
  - **Auto-Next**: Track zu Ende → nächster in der aktuellen Listenreihenfolge
- **Tasten**: `→`/`←` relativ zum spielenden Track, `Enter` fokussierter → selektierter → erster Track, `Leertaste` Pause
- **Kontextmenü** auf Track: Ausschneiden/Kopieren/Einfügen, Alles auswählen, Datei im Explorer anzeigen, Informationen (Popup, Werte kopierbar)
- **Drag & Drop**: Tracks (auch Mehrfachauswahl) in ein Explorer-Fenster ziehen = kopieren, nie verschieben
  - Zahl-Badge am Cursor, normaler Pfeil innen, Kopier-Plus außerhalb
- **Themes**: `Ansicht > Design` — Normal / Alchemist (violetter Akzent, fast schwarz) / Desert (warmer Amber) — jeweils hell/dunkel
- **Zweisprachig**: `Ansicht > Sprache` — Deutsch / English (Neustart)
- **Custom-Scrollbar**: eckige Pille mit Hover, dragbar, Seitenwechsel per Spur-Klick
- **Cache**: `tracks.txt` (Pfad + Metadaten + Tags/Zeile) → Liste + Tags sofort beim Start; Hintergrund-Scan merged neue/gelöschte Tracks inkrementell
- **Persistenz**: `window.txt` (Bounds, Modus, Theme, Sprache), `cols.txt` (Spalten), `root.txt` (Quellordner), `tracks.txt` (Cache)

## Menüs

| Menü | Einträge |
|------|----------|
| **Datei** | Medien neu scannen (`F5`), Quellordner wählen, Beenden |
| **Bearbeiten** | Rückgängig/Wiederherstellen (`Strg+Z`/`Strg+Y`), Ausschneiden/Kopieren/Einfügen (`Strg+X/C/V`), Alles auswählen (`Strg+A`), Seite durchsuchen (`Strg+F`), Zum gespielten Track (`Strg+L`) |
| **Ansicht** | Dunkelmodus, Design, Spalten, Sprache |

## Konfiguration

Musik-Pfad wird über `Datei > Quellordner wählen...` gesetzt und in `root.txt` gemerkt — kein Code-Eingriff nötig.

- **Icons**: Font Awesome Free (`fonts/fa-solid-900.ttf`, SIL OFL), zur Laufzeit ohne Installation geladen; Menü-Icons werden als Bitmaps gerendert

## Hinweise

- **Defender/SmartScreen:** Die selbst gebaute `MyTunes.exe` kann als "ungewöhnlich" markiert werden (unsigniert). Lokal gebaute Dateien sind meist unproblematisch — falls doch: Windows-Sicherheit → Ausnahmen → Ordner ausschließen.
- Der erste Tag-Lauf dauert bei großen Sammlungen etwas (liest alle Dateien einmal), danach kommt alles aus dem Cache.
- Tags nachträglich im Explorer geändert? `tracks.txt` löschen → Neubau.
- Wiedergabe über Windows Media Player (COM) — FLAC o.ä. nur wenn WMP es kann.
- Build-Befehl siehe `MyTunes.cmd` (.NET Framework 4.x, Teil von Windows).
