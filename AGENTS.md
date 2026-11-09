# MyTunes — Agent Notes

## Build

`MyTunes.cmd` baut + startet (csc.exe, .NET Framework 4.x). Oder direkt:

```
csc.exe -nologo -target:winexe -out:MyTunes.exe -r:...WPF/WindowsBase.dll MyTunes.cs
```

## AV/Defender — WICHTIG beim Releasen

- **Nie `powershell.exe`/`-EncodedCommand` oder andere Prozess-Spawns aus der exe aufrufen** — das ist das #1-Pattern, für das Defender die exe löscht (ist hier schon passiert). Tag-Writes laufen über `WriteID3` direkt, nicht über externe Prozesse.
- exe ist **nicht im Repo** (.gitignore) — Releases sollten Source-only bleiben, Nutzer bauen lokal via `MyTunes.cmd`. Lokal gebaute exen werden viel seltener geflaggt als heruntergeladene.
- Für echte Distribution bräuchte es Code-Signing — nicht vorhanden.

## Architektur

- Single-file: `MyTunes.cs` (~1400 Zeilen). Kein NuGet, kein Installer.
- Metadaten: Shell32 `ExtendedProperty` (kanonische `System.Music.*`) → WMP-`getItemInfo`-Fallback → Cache `tracks.txt` (12 Felder/Zeile).
- Tag-Writes: `WriteID3` — eigene ID3v2.3/2.4-Implementierung (TIT2/TALB/TPE1/TPE2/TRCK/TYER/TCON), leer = Frame löschen. Nur mp3/mit-ID3 (Guard verhindert M4A-Korruption).
- Komplexe COM-Leser laufen in 4 STA-Threads (`FillTagChunk`); `RefreshTags` für den gespielten Track.

## Persistenz (alle in .gitignore)

`tracks.txt` (Cache), `window.txt` (Bounds/Theme/Sprache), `cols.txt` (Spalten), `root.txt` (Musik-Ordner).

## Bekannte Warnungen (ok)

`wSaved` ungenutzt, `ApartmentState` deprecated — kosmetisch.
