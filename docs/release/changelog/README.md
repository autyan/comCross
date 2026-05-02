# Release Changelog Directory

This directory stores the version-level changelog files for ComCross.

Release changelogs are repository history documents. They are more detailed
than GitHub Release notes, but they should still avoid development diaries and
implementation noise.

## Naming

Use one Markdown file per public release tag:

```text
docs/release/changelog/v<version>.md
```

Examples:

```text
docs/release/changelog/v0.5.0.md
docs/release/changelog/v0.6.0.md
docs/release/changelog/v1.0.0.md
```

## Scope

Each changelog should record:

- Release goal
- User-facing changes
- Release engineering changes
- Compatibility or migration notes
- Documentation changes
- Known limitations

The matching GitHub Release note in `docs/release/notes/` must link to the
changelog.

Do not add support, compatibility, signing, or security claims that are not
backed by release workflow evidence or authoritative project documentation.
