# Documentation Localization Rules

## Scope

These rules apply when changing GitHub-facing documentation, localized docs, or
documentation rules.

## Authority

- English source documents under `/docs` and the root `README.md` are the
  authoritative project documentation.
- Localized documents are accessibility and orientation aids. They must not add
  product promises, security guarantees, compatibility guarantees, or release
  claims that are absent from the English source.
- If a localized document conflicts with the English source, the English source
  wins until the conflict is reviewed and corrected.

## AI-Assisted Documentation Notice

Localized documents must include a visible notice that they may be generated or
translated with AI assistance.

The notice must say, in the document language when practical:

- the document may be AI-generated or AI-assisted;
- the translation is for accessibility and orientation;
- English source documents are authoritative for conflicts;
- security, signing, release verification, compatibility, and implementation
  claims require extra review.

## Languages

The repository should keep lightweight GitHub-facing entry docs for mainstream
international audiences:

- English
- Simplified Chinese
- Traditional Chinese
- Japanese
- Korean
- Spanish
- French
- German
- Portuguese (Brazil)
- Russian

This list is not a product localization commitment. It is a documentation access
baseline and can expand later.

## Translation Discipline

- Prefer precise, conservative wording over fluent but ambiguous wording.
- Preserve technical identifiers, commands, paths, filenames, environment
  variables, and code snippets exactly unless the English source changes.
- Keep security warnings and compatibility warnings explicit.
- Do not translate proper project names such as `ComCross`, `Core`, `Shell`,
  `PluginHost`, `SessionHost`, and `ExtensionHost`.
- Use localized prose around stable English technical terms when a direct
  translation could create ambiguity.

## Update Expectations

When the root README changes in a way that affects user-facing project status,
quick start, security notices, release verification, or entry-point behavior,
update the localized README files in `docs/i18n/` in the same scope or state why
the translations are intentionally deferred.

When a formal English specification changes, localized GitHub entry docs do not
need a full translation of the spec, but links and short summaries should remain
accurate.

## Release Documentation

- GitHub Release notes are concise public release landing pages stored under
  `docs/release/notes/v<version>.md`.
- Version changelogs are detailed version history files stored under
  `docs/release/changelog/v<version>.md`.
- Final release notes must link to the matching changelog and supporting docs
  instead of duplicating full change history.
- GitHub Release notes are rendered from the release page, not from the
  `docs/release/notes/` directory. Repository document links inside release
  notes must be absolute GitHub `blob` URLs pinned to the published release tag,
  not relative Markdown paths.
- Localized documents must not add release, signing, support, compatibility, or
  security claims beyond the authoritative English release notes, changelog, and
  specifications.

## Verification

For documentation-only localization changes:

```bash
git diff --check
```

For changes that also touch UI strings or i18n resources, run the Shell i18n
guardrails listed in `docs/ai/05-guardrails-verification.md`.
