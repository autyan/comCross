# Prerelease Review Gate

This checklist must be completed before creating a final release branch.

Hotfix package republishes are an explicit exception. When a user-approved
hotfix starts from `main`, fixes one accepted regression, and only republishes
packages under a new hotfix package version, the prerelease review branch and
final release branch are not required. The hotfix must still be reviewed by the
requesting user and validated with the narrowest relevant tests before it is
merged back to `main`.

## Branch Flow

1. Merge all code intended for the release into `develop`.
2. Merge the same release code into `main`.
3. Create a prerelease review branch from `develop`:

   ```bash
   git switch develop
   git pull --ff-only origin develop
   git switch -c feature/v<version>_prerelease
   ```

4. Complete the review checklist on the prerelease branch.
5. Only after the checklist passes, create the final release branch from
   `main`:

   ```bash
   git switch main
   git pull --ff-only origin main
   git switch -c release/v<version>
   ```

## Required Review Checklist

- Current release development state: confirm the release has completed
  implementation convergence and user acceptance.
- Mandatory technical debt: identify whether any debt must be resolved before
  this release, and either resolve it or explicitly defer it outside the
  release.
- Documentation accuracy: verify that repository documentation reflects the
  actual code state, package behavior, support baseline, and known limitations.
- GitHub Actions risk: verify there are no known unhandled risks likely to
  fail the release workflow, including signing secrets, package scripts,
  supported runner behavior, and release note paths.
- Next-stage scope: confirm the next development stage scope is determined and
  documented before the current release is published.
- Release documentation: confirm the final release note and changelog files are
  present and ready:

  ```text
  docs/release/notes/v<version>.md
  docs/release/changelog/v<version>.md
  ```

## Output

The prerelease branch should contain any documentation updates needed to satisfy
the checklist. If the review finds a release-blocking code issue, fix it in the
normal development flow and merge it back into both `develop` and `main` before
creating the release branch.
