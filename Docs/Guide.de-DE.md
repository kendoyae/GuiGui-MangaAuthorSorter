# GuiGui – Benutzerhandbuch

GuiGui ist ein Windows-Programm zum Erkennen von Manga-Autoren und zum Ordnen lokaler Dateien. Es durchsucht einen ausgewählten Speicherort, erkennt Autoren oder Zirkel anhand der Dateinamen und zeigt vor jeder Änderung eine Vorschau an.

## 1. Schnellstart

1. Wählen Sie unter **Quellordner** den zu durchsuchenden Ordner aus.
2. Wählen Sie unter **Zielordner** das Autorenarchiv aus.
3. Legen Sie bei Bedarf Scanbereich, Unterordnersuche und Höchstzahl fest.
4. Klicken Sie auf **Scanvorschau**.
5. Prüfen Sie Ergebnisse, Zielordner und Einträge, die bestätigt werden müssen.
6. Klicken Sie anschließend auf **Dateien ordnen**.

Während der Scanvorschau verschiebt GuiGui keine Dateien. Geeignete Dateien werden erst nach Ihrer Bestätigung geordnet.

## 2. Erkennungsergebnisse

- **Direkte Übereinstimmung:** Der Autor im Dateinamen stimmt mit einem vorhandenen Autorenordner überein.
- **Normalisiert:** Die Namen unterscheiden sich nur durch sichere Zeichen-, Leerzeichen- oder Klammervarianten.
- **Alias-Treffer:** Der Autor wurde über Aliase in der Autoren-Entitätenbibliothek gefunden.
- **Zirkel-/Autorentreffer:** Ein Zirkel oder Autor wurde aus einem strukturierten Dateinamen erkannt.
- **Neuer Autor:** Es wurde kein vorhandener Ordner gefunden; ein neuer Autorenordner ist vorgesehen.
- **Bestätigung erforderlich:** Mehrere Kandidaten oder zu wenige Informationen erfordern eine manuelle Auswahl.
- **Nicht erkannt:** Der Autor kann nicht zuverlässig bestimmt werden; die Datei wird nicht automatisch geordnet.
- **Ausgeschlossen:** Die Datei entspricht einer Ausschlussregel und wird nicht verarbeitet.

## 3. Einträge manuell bestätigen

Ein nicht eindeutig oder nicht erkannter Eintrag kann einem vorhandenen Autor, einem neuen Autor oder dem Ausschluss zugewiesen werden. GuiGui wählt bei mehreren Kandidaten aus Sicherheitsgründen nicht automatisch aus.

## 4. Häufig verwendete Einstellungen

- **Dateitypprofile:** Legen fest, welche Dateiendungen gescannt werden.
- **Tag-Bereinigungsregeln:** Entfernen Dateinamen-Tags, die keinen Autor bezeichnen.
- **Scan-Ausschlussregeln:** Überspringen Dateien, die nicht geordnet werden sollen.
- **Autoren-Entitätenbibliothek:** Verwaltet Autoren, Gruppen, Aliase, Beziehungen, öffentliche Anpassungen und Konflikte in AuthorEntities.json.
- **Archiveinstellungen:** Steuern Gruppengröße, Ordnernamen und freien Sicherheitsbereich auf dem Datenträger.

## 5. Everything

Die Everything-Integration ist optional. Wenn sie verfügbar ist, beschleunigt GuiGui damit die Dateisuche. Andernfalls wird automatisch die Windows-Dateisystemsuche verwendet; die Grundfunktionen bleiben erhalten.

## 6. Daten und Sicherheit

Benutzereinstellungen, Autoren-Aliase, Verlauf und Regeldateien werden beim ersten Gebrauch oder Speichern im Programmordner angelegt. Bewahren Sie diese Dateien beim Aktualisieren oder Verschieben der EXE auf.

Prüfen Sie vor dem Ordnen stets die Vorschau. Testen Sie bei vielen Dateien zunächst mit einer kleinen Auswahl und bewahren Sie erforderliche Sicherungen auf.

## 7. Nach Updates suchen

Wählen Sie **Hilfe → Nach Updates suchen** oder **Über GuiGui**, um GitHub Releases zu prüfen. GuiGui zeigt offizielle Versionsinformationen an und öffnet die offizielle Downloadseite; die EXE wird nicht automatisch heruntergeladen, installiert oder überschrieben.

## 8. Hilfemenü

- **Benutzerhandbuch:** Öffnet dieses Handbuch.
- **Versionshinweise:** Zeigt Neuerungen, Fehlerbehebungen und Verbesserungen jeder Version.
- **Leistungsdiagnose:** Analysiert die Scanleistung; für den normalen Gebrauch werden die Standardeinstellungen empfohlen.
- **Über GuiGui:** Zeigt Version, Projektseite, Lizenz und Feedbackmöglichkeiten.

## Autoren-Entitätenbibliothek (V1.12.10)

In AuthorEntities.json Autoren und Gruppen hinzufügen, Standardnamen, Umschriften, Aliase, deaktivierte Namen und Bestätigungen bearbeiten. Ein gelöschter Alias entfernt keinen Autor. Vor dem Löschen einer Entität ihre Beziehungen aufheben.

Öffentliche Datensätze sind schreibgeschützt. Benutzeranpassungen ändern den wirksamen Namen, ergänzen Aliase oder deaktivieren öffentliche Namen. Wiederherstellen entfernt die Anpassung. Bei fehlender oder geteilter Identität nach einer Aktualisierung Konflikte prüfen und die Anpassung ausdrücklich neu zuordnen. Lokale Daten bleiben erhalten.

Namenskonflikte durch Kandidatenbestätigung, unabhängige Entitäten, offene Entscheidung oder Namensdeaktivierung behandeln. Beziehungen zu Benutzergruppen separat bearbeiten. Alte TXT-Dateien dienen nur dem Import. AuthorEntities.json, Sicherungen, Einstellungen, öffentliche Datenbank und Cache beim Aktualisieren behalten. [Hinweise und Prüfprotokolle](UNIFIED_AUTHOR_LIBRARY.md).