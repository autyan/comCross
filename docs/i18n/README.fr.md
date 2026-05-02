# ComCross

> Avis de documentation : ce document peut avoir été généré ou traduit avec
> l'aide de l'IA. Il sert de référence d'accessibilité et d'orientation. En cas
> de conflit avec le README anglais ou les spécifications formelles, le texte
> source anglais fait autorité.

ComCross est une boîte à outils de communication embarquée multiplateforme pour
les flux série, TCP et UDP.

## État

ComCross est encore en phase pré-stable. Les changements incompatibles sont
autorisés lorsqu'ils corrigent les limites d'architecture, les répertoires
d'exécution, la disposition des plugins ou les contrats de packaging. Ces
changements doivent être documentés.

## Capacités principales

- Adaptateurs série, TCP et UDP fournis sous forme de plugins isolés.
- Gestion des sessions et des workloads avec descripteurs persistés.
- Flux RX/TX recherchable avec attributs de trame bornés.
- Pages de paramètres de plugins et état d'interface produit par les plugins.
- Localisation intégrée en anglais et chinois simplifié pour Shell.

## Démarrage rapide pour le développement

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## Notes importantes

- Le futur point d'entrée utilisateur est prévu comme `ComCross.Startup`.
- Les versions publiques Windows peuvent d'abord utiliser un certificat de test
  auto-signé. Windows ne fait pas confiance à ce certificat par défaut.
- Avant d'ignorer les avertissements du système, vérifiez les checksums et la
  signature de la release.
- La signature des paquets de plugins officiels sera séparée de la signature de
  code Windows et de la signature des artefacts de release.

## Références

- README racine : [../../README.md](../../README.md)
- Index des spécifications : [../specs/00-Index.md](../specs/00-Index.md)
- Conception Startup, identité d'instance et signature :
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
