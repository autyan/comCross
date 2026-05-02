# ComCross

> Documentation notice: this localized document may be generated or assisted by
> AI. It is provided for accessibility and orientation. If it conflicts with the
> root English README or formal specifications, use the English source as the
> authoritative text.

ComCross is a cross-platform embedded communication toolbox for serial, TCP,
and UDP workflows.

## Status

ComCross is in a pre-stable stage. Breaking changes are still allowed when they
fix architecture boundaries, runtime directories, plugin layout, or packaging
contracts. Such changes must be documented.

## Main Capabilities

- Serial, TCP, and UDP bus adapters delivered as isolated plugins.
- Session and workload management with persisted descriptors.
- Searchable RX/TX message stream with bounded frame attributes.
- Plugin settings pages and plugin-produced UI state.
- Built-in English and Simplified Chinese Shell localization.

## Development Quick Start

```bash
dotnet restore
dotnet build ComCross.sln --no-restore
dotnet run --project src/Shell/ComCross.Shell.csproj
dotnet test ComCross.sln --no-build
```

## Important Notes

- The future user-facing entry point is planned to be `ComCross.Startup`.
- Windows public releases may initially use a self-signed test certificate. That
  certificate is not trusted by Windows by default.
- Users should verify release checksums and release signatures before bypassing
  operating-system warnings.
- Official plugin package signing will be separate from Windows code signing
  and release artifact signing.

## References

- Root README: [../../README.md](../../README.md)
- Specification index: [../specs/00-Index.md](../specs/00-Index.md)
- Startup, identity, and signing design:
  [../specs/12-Startup-Identity-Signing-Design.md](../specs/12-Startup-Identity-Signing-Design.md)
