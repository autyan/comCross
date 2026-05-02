# ComCross

> Dokumentationshinweis: Dieses Dokument kann mit KI-Unterstützung erstellt
> oder übersetzt worden sein. Es dient der Orientierung und Barrierefreiheit.
> Bei Widersprüchen zum englischen README oder zu formalen Spezifikationen gilt
> der englische Quelltext.

ComCross ist ein plattformübergreifendes Embedded-Kommunikationswerkzeug für
serielle, TCP- und UDP-Workflows.

## Status

ComCross befindet sich noch vor einer stabilen Kompatibilitätsphase. Breaking
Changes sind erlaubt, wenn sie Architekturgrenzen, Laufzeitverzeichnisse,
Plugin-Layout oder Packaging-Verträge korrigieren. Diese Änderungen müssen
dokumentiert werden.

## Hauptfunktionen

- Serielle, TCP- und UDP-Busadapter als isolierte Plugins.
- Sitzungs- und Workload-Verwaltung mit persistierten Deskriptoren.
- Durchsuchbarer RX/TX-Nachrichtenstrom mit begrenzten Frame-Attributen.
- Plugin-Einstellungsseiten und von Plugins erzeugter UI-Zustand.
- Eingebaute englische und vereinfachte chinesische Lokalisierung für Shell.

## Schnellstart für Entwicklung

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## Wichtige Hinweise

- Der zukünftige Einstiegspunkt für Benutzer ist als `ComCross.Startup` geplant.
- Öffentliche Windows-Versionen können anfangs ein selbstsigniertes
  Testzertifikat verwenden. Windows vertraut diesem Zertifikat standardmäßig
  nicht.
- Prüfen Sie Release-Checksummen und Signaturen, bevor Sie Warnungen des
  Betriebssystems umgehen.
- Die Signatur offizieller Plugin-Pakete ist von Windows-Code-Signaturen und
  Release-Artefakt-Signaturen getrennt.

## Referenzen

- Root README: [../../README.md](../../README.md)
- Spezifikationsindex: [../specs/00-Index.md](../specs/00-Index.md)
- Startup-, Instanzidentitäts- und Signaturdesign:
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
