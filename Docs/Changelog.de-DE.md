## V1.13.10 — Leistungsverbesserung beim Zusammenführen des Autorenindex
- Normale Updates installieren den vollständigen öffentlichen Index direkt, ohne eine Werkbeleg-Datenbank zu erstellen, zu lesen oder zu vergrößern; Belege bleiben dem erweiterten Quellen-Build vorbehalten.
- Fehlende Indizes ergänzen und wiederholte Abfragen bei großen Tag-Datenbanken durch Gruppierung ersetzen.
- Neue Aktion zum erneuten Zusammenführen bereits importierter Belege, ohne erneuten Download.
- Pause, Fortsetzen und Abbruch auch während SQLite-Zusammenführung; laufende Zeit anzeigen.
- Schema v4 und Benutzerdaten unverändert. Windows-Build und große Quellen noch zu testen.

## V1.13.9 — Kompilierungsfehler der erweiterten Download-Schnittstelle
- Optionalen Steuerungsparameter bei `DownloadAsync` ergänzt; behebt CS0103 und CS1501.
- Pause, Fortsetzen und Abbrechen bleiben erhalten. Der vollständige Windows-Build muss noch geprüft werden.

## V1.13.8 — Pfade der Compiler-Quelldateien
- CS1504 bei fünf Quelldateien des erweiterten Builders durch Windows-Pfade in CompilerSources.rsp behoben.
- Vor der Kompilierung alle Quelldateien, doppelte Einträge und die Übereinstimmung mit der Projektdatei prüfen.
- Keine Funktionsänderung gegenüber V1.13.7; Windows-Kompilierung noch zu prüfen.

## V1.13.7 — Drei Quellen für den erweiterten Aufbau
WinForms-Quellkarten, EH-Tagdaten, inkrementelles CSV und explizite Schema-v4-Basis; Windows-Tests stehen noch aus.

## V1.13.6 — Öffentlicher Index und Vollaufbau

- Offizieller Datenbank-Download mit SHA-256-Prüfung und lokalem Import.
- Vollständiger CSV-Import mit leerer Schema-v4-Basis falls nötig.
- Neue offizielle Basis plus lokale Belege nach Neustart aktivieren.
- Keine .NET-8-/WPF-Laufzeit erforderlich; Manifest muss veröffentlicht werden.

## V1.13.5 — Inkrementelle Zusammenführung der Autoren-/Gruppendatenbank

- Monatliche CSV-Daten werden mit den gemeinsamen Filterregeln in den öffentlichen Index integriert.
- Aus einem offiziellen Basisstand wird eine neue Datenbank aufgebaut; Aktivierung beim Neustart.
- Bestätigte Benutzerdaten bleiben geschützt; mehrdeutige Identitäten werden nicht automatisch zusammengeführt.

## V1.13.4 — Startabsturz und Sprachprüfung behoben

- Startabsturz durch einen doppelten Schlüssel im integrierten englischen Wörterbuch behoben.
- Vor dem Kompilieren doppelte Schlüssel und die Übereinstimmung mit den offiziellen Sprachdateien prüfen.

## V1.13.3 — GitHub-Werkbelege

- Detail-Schaltfläche umbenannt; nHentai API v2-Abfragen entfernt.
- Ferngesteuerte GitHub-CSV-Aktualisierung per SHA; Werkbelege separat in SQLite.
- Keine automatische Identitätszusammenführung; öffentliche Datenbank bleibt schreibgeschützt.

## V1.13.2 — Korrekturen in der Autorenverwaltung

- Bei öffentlichen Daten „Vorherige Seite“ links und „Nächste Seite“ rechts anordnen.
- Fehler bei ungültiger Auswahl beim Öffnen der Autoren-Gruppen-Beziehungen beheben.

## V1.13.1 — Einheitliche Autorenverwaltung

- Autoren und Gruppen gemeinsam suchen und bearbeiten; Aliasnamen, Quellen und Beziehungen in einer Detailansicht anzeigen.
- Die Bibliothek auf drei Hauptseiten reduzieren: Autoren und Gruppen, Konflikte und Prüfung sowie Datenpflege.
- Die öffentliche Datenbank bleibt schreibgeschützt; persönliche Änderungen als Benutzeranpassungen speichern.
- Seitennavigation für öffentliche Daten ergänzen und Ordnernamenregeln in die Archiveinstellungen verschieben.

## V1.13.0 — Dateinamenstruktur und Veranstaltungspräfixe

- Identitäten nach unbekannten Präfixen vor [Gruppe (Autor)] erkennen; vermutete Veranstaltungen aus der Bewertung ausschließen und explizite Autoren stärker gewichten.
- Gemeinsame Präfixe anhand verschiedener Gruppen-/Autorenpaare nur für den Scan erkennen; bestätigte Identitäten und Konfliktprüfungen erhalten.
- Werktitel recherchieren, Vorschläge zur Bestätigung für die Offline-Nutzung speichern, Titel nicht als Autorenalias lernen und Erkennungsgründe anzeigen; Cache-Version erhöhen.

## V1.12.13 — Fensterwechsel bei Online-Einstellungen und Entitäten

- Online-Einstellungen als einzelnes nichtmodales Fenster öffnen; Verwaltungsfenster dem Hauptfenster zuordnen und verdeckte modale Blockierung vermeiden.
- Bestehende Fenster wiederverwenden, minimierte Entitätenbibliothek wiederherstellen sowie Speichern, Abbrechen und gemeinsames Schließen erhalten.
- Isolierte Fensterregression für Bedienbarkeit, Wiederöffnen, Speichern, Abbrechen und Schließen ergänzen.

## V1.12.12 — Öffentliche Datenbank Schema v4

- Kompatibilitätsansichten und ArtistGroup-Beziehungen lesen, ohne interne Data-Tabellen oder die Wartungsdatenbank zu öffnen.
- DanbooruArtistTag für Identitäten, deaktivierte Anpassungen und Cache-Abhängigkeiten übernehmen; ältere Datenbanken ohne diese Spalte unterstützen. Source über die Ansicht und ExternalId mit Provider-/Rollenbereich lesen.
- Ansichtentests und schreibgeschützte Prüfung der echten Builder-Ausgabe ergänzen.

## V1.12.11 — Scan-Blockierung bei öffentlichen Anpassungen

- Dateiabhängigkeiten aus geladenen Namen und Alias-Schlüsseln bilden, ohne öffentliche Identitäten je Datei erneut abzufragen.
- Standard-Bereinigungsregeln einmal kompilieren, statt Regexe für jede Dateiabhängigkeit neu zu erstellen.
- Plan-Cache-Lesen abbrechbar machen; abgebrochene Schreibtransaktionen zurückrollen und Abbruch weiterreichen.
- Regression mit 8.490 Dateien und öffentlicher Anpassung sowie Abbruchprüfungen ergänzen.

## V1.12.10 — Einheitliche Autorenverwaltung und öffentliche Anpassungen

- Separate Aliasbibliothek entfernt. AuthorEntities.json verwaltet Autoren, Gruppen, Aliase, Beziehungen, Konflikte, öffentliche Anpassungen, Import/Export und Abfragestatus.
- Öffentliche Datenbank bleibt schreibgeschützt. Eindeutige stabile Identitäten behalten Anpassungen über Versionen hinweg; fehlende/geteilte Identitäten benötigen eine ausdrücklich bestätigte Neuzuordnung.
- Erkennung und gespeicherte Pläne werden anhand der betroffenen Dateiabhängigkeiten ungültig; der Quelldateiindex bleibt erhalten. Temporäre Online-Entitäten können sicher gespeichert werden.
- Migration mit 8.490 echten Dateien ohne Identitäts-/Zielabweichung; Wiederholung trifft alle 8.490 Cache-Einträge ohne Neuberechnung. Sechs historische Identitätskonflikte benötigen nun Bestätigung; Folgeänderungen sind dokumentiert.
- Anleitung und Messprotokolle: Docs/UNIFIED_AUTHOR_LIBRARY.md und Docs/AuthorLibraryAcceptance. Version: 1.12.10.0.

## V1.12.8 — Schnelle Leertreffer und inkrementeller Plan-Cache

- Leere Everything-Abfragen anhand des indizierten Verzeichnisses prüfen; nur bei unsicherer Abdeckung das Dateisystem erneut durchsuchen.
- Bei vollständigem Cache-Treffer keine Pläne erneut schreiben; nur neu berechnete Datensätze speichern und SQLite-Statements wiederverwenden.
- Alte Abfragezeiten und Deltas aus dem Vorwärm-Cache nicht der aktuellen Diagnose zuordnen; Lese-, Schreib- und Abschlusszeiten getrennt erfassen.

## V1.12.6 · Anzeige des Leistungsverlaufs korrigiert

- Die Hintergrundvorbereitung überschreibt nach einem Neustart nicht mehr die Details eines ausgewählten früheren Scans.
- Das Kopieren übernimmt stets den ausgewählten historischen Leistungsbericht.
- Beim Löschen des Leistungsverlaufs wird auch die Protokolldatei geleert; bei einem Fehler bleiben die Einträge erhalten.
- Ein Regressionstest prüft die Wiederherstellung des Verlaufs nach einem Neustart. Scan und Erkennung bleiben unverändert.

## V1.12.6

- Wiederholte vollständige Zielautorenindex-Aufbauten durch inkrementelle Ergänzungen ersetzt.
- Diagnose der Phasen und Indexergänzungen bei echten Scans hinzugefügt (GL4, kompatibel mit GL3).
- Regressionstests für identische Treffer bei inkrementellem und vollständigem Index. Sprachschema 75.

## V1.12.5

- Normale Autorenabfragen aus `GuiGuiAuthorIndex.db` verwenden nun einen einmal aufgebauten, schreibgeschützten Identitätsindex im Speicher statt mehrerer SQLite-Abfragen pro neuem Autor; GuiGui wärmt diesen Index im Hintergrund vor.
- Der Simulations-Benchmark unterstützt jetzt die Anzahl eindeutiger Autoren sowie kalte und vorgewärmte Autorenindizes, damit Wiederholungen, Kaltstart und stabiler Betrieb getrennt gemessen werden können.
- Der Bericht zeigt nun Aufbauzeit und Größe des Autorenindex, sodass Datenbankvorbereitung und eigentliche Erkennungszeit getrennt bewertet werden können.
- Rohe Sprachschlüssel in Teilen der Leistungsdiagnose und des Simulationsfortschritts wurden behoben; das offizielle Sprachschema ist jetzt Version 74.

## V1.12.4

- Der Simulations-Benchmark zeigt laufend Phase, Fortschritt, aktuelles Element und verstrichene Zeit.
- Das Simulationsfenster ist nun unabhängig und nicht modal; Hauptfenster und Leistungsdiagnose bleiben bedienbar.

## V1.12.3

- Ein Fehler beim Öffnen des Simulationsfensters auf bestimmten DPI-/Layout-Konfigurationen durch eine zu frühe SplitContainer-Positionierung wurde behoben.

## V1.12.2

- Ein vollständig speicherbasierter Simulations-Benchmark wurde ergänzt. Er liest `GuiGuiAuthorIndex.db` nur als Testdatenquelle und misst Erst-, Cache- und inkrementelle Scans, ohne echte Dateien oder produktive Caches zu verändern.

## V1.12.1

- Der Quellindex wurde auf ein Modell aus persistentem FileIndex, inkrementellen Änderungen, Recognition Cache und Query/View-Projektionen umgestellt.
- Das Umschalten der Unterordnersuche ist nun nur noch eine Indexprojektion und löst keine erneute Erkennung bereits zwischengespeicherter Dateien aus.
- Unveränderte Dateien werden vor Analyse und Erkennung ausgeschlossen; reine Verschiebungen behalten Erkennungsdaten, echte Umbenennungen machen nur die betroffene Datei ungültig.
- Größen- und Zeitangaben werden direkt aus dem Everything-Index übernommen, sodass nicht für jedes Ergebnis erneut Dateimetadaten gelesen werden müssen.
- Von GuiGui ausgeführte Verschiebungen und Umbenennungen aktualisieren den persistenten Index und die aktuelle Scan-Sitzung sofort.
- Die Scandiagnose zeigt nun Indextreffer, Added/Modified/Moved/Renamed/Deleted-Änderungen, Plan-Cache-Treffer und neu berechnete Dateien.

## V1.12.0

- Benutzerhandbuch und Versionshinweise sind nun auf Chinesisch, Englisch und Deutsch verfügbar und folgen der Oberflächensprache.
- Die Filterleiste passt sich besser an unterschiedliche Fensterbreiten an.
- Zu prüfende Einträge blockieren andere Dateien nicht mehr; sie werden vor dem Sortieren gemeldet und übersprungen.
- Zielnamenskonflikte werden früh erkannt und unter „Duplikate“ sowie „Prüfen“ angezeigt.
- Hinweise zu nicht ausführbaren Sortiervorgängen wurden verständlicher gestaltet.

## V1.11.30

- Untergeordnete Fenster zeigen kein Programmsymbol mehr in der Titelleiste; das Symbol des Hauptfensters bleibt unverändert.

## V1.11.29

- Die öffentliche Marke wurde als „GuiGui“ vereinheitlicht und die Programmdatei in `GuiGui.exe` umbenannt.
- Das 64×64-Programmsymbol auf der Über-Seite wurde zum Informationsbereich zentriert.

## V1.11.28

- Die Mengenbeschreibung wurde in einen Tooltip verschoben, um Platz im Hauptfenster zu sparen.
- Das Kontextmenü der Hauptliste erhielt Pfadbefehle und statusabhängige Autorenaktionen.
- Comic-Inhaltstags und Bereinigungsregeln für CSP-, Reitaisai- und Ragna-Festival-Nummern wurden ergänzt.
- Namen mit Pluszeichen wie `Xration+(mil)` können nun vorhandenen Zirkelordnern zugeordnet werden.

## V1.11.27

- Mengenfelder zeigen nun den Hinweis „0 = alle“.
- Änderungen der Spaltenbreite werden schon beim Ziehen angezeigt.
- Bereinigungsregeln für ComiTre- und Tora-Festival-Nummern wurden ergänzt.

## V1.11.26

- Listenspalten lassen sich unabhängig ändern, ohne benachbarte Spalten zusammenzudrücken; rechts darf Freiraum bleiben.
- Versionshinweise öffnen direkt mit dem Versionsinhalt.

## V1.11.25

- Benutzerhandbuch, Versionshinweise, Lizenz und Support-QR-Codes sind eingebettet, damit die einzelne EXE vollständig nutzbar bleibt.
- Ein vollständiges Handbuch wurde ergänzt und die Versionshistorie von der Entwicklerdokumentation getrennt.
- Die Ablaufhilfe links unten wurde in drei nummerierte Schritte vereinheitlicht.
- Für mehrdeutige und nicht erkannte Ergebnisse kann ein Autorenordner direkt gewählt werden.
- „Suchen“ wurde in „Filtern“ umbenannt und unterstützt mehrere Suchbegriffe.
- Die Erkennung alter Namen, aufeinanderfolgender Tags, unmarkierter Namen und vorangestellter Veranstaltungsangaben wurde verbessert.
- Standardregeln für häufige Inhaltstags und Dōjin-Veranstaltungsnummern wurden ergänzt.

## V1.11.24

- Die Suche nach dem neuesten offiziellen GitHub-Release samt Hinweisen wurde ergänzt.
- Scandiagnose, Layout und mehrsprachige Darstellung wurden verbessert.

## V1.11.23

- Wiederholte Scans, Filterwechsel und große Listen wurden beschleunigt.
- Hintergrundvorbereitung verkürzt die Wartezeit beim Scannen.
- Mehr gebräuchliche Autoren- und Zirkelnamen werden erkannt.
- Darstellungsfehler in Archiv-, Dateityp- und Spracheinstellungen wurden behoben.

## V1.11.22

- Große Scans und Ausschlussregeln wurden beschleunigt.
- Everything- und Dateisystemscans wurden effizienter.
- Unvollständige englische und deutsche Texte in Einstellungsfenstern wurden korrigiert.

## V1.11.21

- Deutsch wurde als offizielle Oberflächensprache ergänzt.
- Größen von Schaltflächen, Beschriftungen und Listen wurden für mehrere Sprachen verbessert.
- Die Erkennung von Namen mit Leerzeichen, Klammern oder alternativen Zeichenformen wurde korrigiert.

## V1.11.20

- Das mehrsprachige Layout wurde im gesamten Programm verbessert.
- Abschneiden und Fehlausrichtung bei verschiedenen Anzeigeskalierungen wurden behoben.

## V1.11.19

- Eine schrittweise Einführung für die erste Nutzung wurde ergänzt.
- Supportseite und QR-Code-Darstellung wurden verbessert.

## V1.11.18

- Die Supportseite wurde für übersichtlichere Zahlungs- und Online-Supportoptionen neu gestaltet.
- Das Layout von Einstellungsseiten und Listen wurde vereinheitlicht.

## V1.11.15–V1.11.17

- Über-, Lizenz-, Community- und Supportseiten wurden verbessert.
- Sprach- und Skalierungsprobleme in mehreren Fenstern wurden behoben.

## V1.11.10–V1.11.14

- Unbestätigte Einträge blockieren andere bestätigte Dateien nicht mehr.
- Ein klarerer Bestätigungsablauf und eine Prüfung vor der Ausführung wurden ergänzt.
- Meldungen zu Duplikaten, Zielkonflikten und Ergebnissen wurden verbessert.
- Über- und Supporteinträge wurden getrennt.

## V1.11.6–V1.11.9

- Filter für ausgeschlossene, unbestätigte und doppelte Einträge wurden ergänzt.
- Autorenzuweisung, Dateiausschluss und Ergebnisanzeige wurden in die Vorschau aufgenommen.
- Leistung großer Listen, Spaltenbreitenspeicherung und Tabellenbedienung wurden verbessert.
- Das Projekt wurde neu geordnet und eine getrennte Entwicklerdokumentation eingerichtet.

## V1.11.0–V1.11.5

- Autorenentitäten und ein lokaler Abfragecache wurden ergänzt.
- Tag-Bereinigungs- und globale Scan-Ausschlussregeln wurden ergänzt.
- Weitere Mehrfachautoren-, Zirkel- und komplexe Dateinamenformate werden unterstützt.
- Hintergrundausführung, Fortschrittsanzeige und sicheres Abbrechen wurden ergänzt.

## Frühere Versionen

- Autorenerkennung, Aliasabgleich, Archivvorschau, Dateiverschiebung und Verlauf wurden eingeführt.
- Dateinamenkompatibilität, Ordnerabgleich, Ausführungssicherheit und Bedienung wurden laufend verbessert.
