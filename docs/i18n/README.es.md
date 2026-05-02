# ComCross

> Aviso de documentación: este documento puede haber sido generado o traducido
> con ayuda de IA. Se ofrece como referencia de accesibilidad y orientación. Si
> entra en conflicto con el README en inglés o con las especificaciones
> formales, prevalece el texto fuente en inglés.

ComCross es una caja de herramientas de comunicación embebida y multiplataforma
para flujos de trabajo con serial, TCP y UDP.

## Estado

ComCross aún está en una etapa previa a la compatibilidad estable. Se permiten
cambios incompatibles cuando corrigen límites de arquitectura, directorios de
ejecución, distribución de plugins o contratos de empaquetado. Esos cambios
deben documentarse.

## Capacidades principales

- Adaptadores de bus serial, TCP y UDP entregados como plugins aislados.
- Gestión de sesiones y cargas de trabajo con descriptores persistidos.
- Flujo RX/TX consultable con atributos de trama acotados.
- Páginas de configuración de plugins y estado de UI producido por plugins.
- Localización integrada en inglés y chino simplificado para Shell.

## Inicio rápido para desarrollo

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## Notas importantes

- El futuro punto de entrada para usuarios está planeado como `ComCross.Startup`.
- Las versiones públicas para Windows pueden usar inicialmente un certificado de
  prueba autofirmado. Windows no confía en ese certificado de forma predeterminada.
- Antes de omitir advertencias del sistema operativo, verifique los checksums y
  la firma de la publicación.
- La firma de paquetes oficiales de plugins estará separada de la firma de código
  de Windows y de la firma de artefactos de release.

## Referencias

- README raíz: [../../README.md](../../README.md)
- Índice de especificaciones: [../specs/00-Index.md](../specs/00-Index.md)
- Diseño de Startup, identidad de instancia y firma:
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
