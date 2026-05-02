# Git And Collaboration Rules

## Dirty Worktrees

- The worktree may contain user changes.
- Never revert user changes unless explicitly asked.
- If user changes overlap the intended scope, inspect them and work with them.
- If user changes are unrelated, leave them alone.

## Commit Scope

- Prefer one complete, reviewable scope per commit.
- Do not mix architecture refactors, i18n cleanup, behavior changes, formatting churn, and documentation changes unless the user explicitly scopes them together.
- Commit messages should describe the scope, not the implementation mechanics alone.

## User Acceptance

- When the user asks for commit-by-commit collaboration, implement and verify the scope, then wait for user acceptance before committing.
- After a commit, report the commit hash, verification, residual risk, and the proposed next scope.
- Do not continue into the next scope without user confirmation.

## Release-Sensitive Work

- Do not push, tag, publish, package, or alter release branches unless explicitly requested.
- Do not change runtime or publish layout as part of unrelated refactors.
- Before creating a release branch, ensure the release code has already been
  merged into both `main` and `develop`.
- After `main` and `develop` contain the release code, create a prerelease
  review branch from `develop` using the pattern
  `feature/v<version>_prerelease`.
- Complete and document the prerelease review before opening the release
  branch. The required checklist lives in
  `docs/release/prerelease-review.md`.
- Final release branches must carry both concise GitHub Release notes under
  `docs/release/notes/v<version>.md` and complete version changelogs under
  `docs/release/changelog/v<version>.md`.
- GitHub Release notes must stay short and link to the matching changelog
  instead of duplicating full version history.
- Validation-only draft pre-releases may omit release notes, but they must be
  deleted after validation.

## Verification Before Commit

For code changes, run at least:

```bash
dotnet build ComCross.sln --no-restore
```

For boundary-sensitive changes, also run:

```bash
bash repo-tools/check-project-boundaries.sh
```

For Shell i18n work, run the i18n guardrails too.
