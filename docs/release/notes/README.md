# Release Notes Directory

This directory stores the release note source files used by GitHub Releases.

## Naming

Use one Markdown file per public release tag:

```text
docs/release/notes/v<version>.md
```

Examples:

```text
docs/release/notes/v0.5.0.md
docs/release/notes/v0.6.0.md
docs/release/notes/v1.0.0.md
```

Release candidates may use their own files only when they need public notes:

```text
docs/release/notes/v0.5.0-rc.1.md
```

Validation-only draft pre-releases do not need a release notes file.

## Required Sections

Final release notes should include:

- Release summary
- Supported operating systems and package formats
- Installation and upgrade notes
- Compatibility or breaking changes
- Security, signing, and checksum verification notes
- Known limitations
- Contributors or acknowledgements when applicable

Do not make support, compatibility, signing, or security claims in a release
note unless the claim is also true in the code, package scripts, and release
verification evidence.

## Workflow Integration

The GitHub Actions release workflow accepts:

```text
release_notes_path
```

For final non-draft releases, this input is required and must point to a file in
this directory. Draft or validation-only pre-releases may omit it.

Example final release trigger:

```bash
gh workflow run release.yml \
  -f version=0.5.0 \
  -f prerelease=false \
  -f draft=false \
  -f require_signing=true \
  -f release_notes_path=docs/release/notes/v0.5.0.md
```

Example validation trigger:

```bash
gh workflow run release.yml \
  -f version=0.5.0-rc.1 \
  -f prerelease=true \
  -f draft=true \
  -f require_signing=true
```
