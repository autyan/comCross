# Release Notes Directory

This directory stores the concise release note source files used by GitHub
Releases.

Release notes are the public download page content. Keep them short. Put
complete version history in `docs/release/changelog/` and link to it from each
final release note.

## Naming

Use one Markdown file per public release tag:

```text
docs/release/notes/v<version>.md
```

Examples:

```text
docs/release/notes/v0.3.1.md
docs/release/notes/v0.3.2.md
docs/release/notes/v0.4.0.md
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

Final release notes should include only:

- One short release summary
- Download/package selection guidance
- Checksum/signature verification commands
- Important compatibility or support notes
- Links to the full changelog and supporting docs

GitHub Release notes are rendered outside the repository file tree. Links from
release notes to repository documents must therefore use absolute GitHub `blob`
URLs pinned to the published release tag. Do not use Markdown paths such as
`../changelog/v0.6.0.md` in files that will be passed to `release_notes_path`.

Do not make support, compatibility, signing, or security claims in a release
note unless the claim is also true in the code, package scripts, and release
verification evidence.

Avoid detailed implementation history, architecture discussion, and full
changelogs in this directory.

Historical releases should be backfilled into this directory when practical so
old GitHub Releases can be normalized to the same concise style. Do not add
modern signing, package, or support claims to historical notes unless those
claims were true for that release.

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
  -f version=0.6.0 \
  -f prerelease=false \
  -f draft=false \
  -f require_signing=true \
  -f release_notes_path=docs/release/notes/v0.6.0.md
```

Example validation trigger:

```bash
gh workflow run release.yml \
  -f version=0.6.0-rc.1 \
  -f prerelease=true \
  -f draft=true \
  -f require_signing=true
```
