# Bilder-Umbenenner

Kleines Windows-Programm zum massenweisen Umbenennen von Bildern.

**Download:** [`dist/BilderUmbenenner.exe`](dist/BilderUmbenenner.exe) – einfach starten, keine Installation nötig
(läuft auf Windows 10/11, nutzt das vorinstallierte .NET Framework 4).

## Bedienung

1. Bilder oder ganze Ordner (inkl. Unterordner) ins Fenster ziehen – oder auf das Programmsymbol ziehen,
   oder über „Dateien hinzufügen...“.
2. In der Liste siehst du Erstell- und Änderungsdatum (das verwendete ist **fett**) und den neuen Namen.
3. „Alle umbenennen“ klicken. Mit „Rückgängig“ lässt sich der letzte Durchgang zurücknehmen.

## Namensregel

- Neuer Name: `yyyymmdd_hhmm` aus dem **früheren** von Erstell- und Änderungsdatum, z. B. `20210503_1407.jpg`.
- Die Dateiendung bleibt erhalten.
- Mehrere Bilder in derselben Minute im selben Ordner: `20210503_1407.jpg`, `20210503_1407_1.jpg`, `20210503_1407_2.jpg` …
  (in zeitlicher Reihenfolge).
- Andere, bereits vorhandene Dateien werden nie überschrieben.
- Nur Bilddateien werden übernommen (jpg, png, gif, bmp, tif, webp, heic, gängige RAW-Formate …).

## Selbst bauen

`./build.sh` (unter Linux mit Mono) oder unter Windows mit `csc.exe` aus dem .NET Framework:

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:BilderUmbenenner.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
```
