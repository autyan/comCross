# Compatibility Policy

This document records the current compatibility stage for ComCross.

## Current Stage

ComCross is currently in a pre-stable, pre-public-adoption stage.

The project has not committed to backward compatibility for runtime directories,
configuration locations, database locations, plugin package locations, install
layout, or release package layout.

## Allowed Breaking Changes

Breaking changes are allowed when they correct architecture boundaries, runtime
directory contracts, persistence placement, plugin layout, packaging behavior, or
other long-lived product contracts.

Examples include:

- moving runtime plugin packages to a new directory;
- moving configuration, database, log, cache, or local data files;
- changing installer-owned directories;
- changing package payload layout;
- discarding development-era persisted state.

## Required Disclosure

Breaking changes must not be silent.

Each breaking change must name its compatibility impact in at least one durable
surface:

- public documentation;
- AI/development workflow documentation;
- commit or pull request description;
- verification notes;
- logs or tests when useful for runtime observability.

## Migration Policy

During the current stage, compatibility reads and one-time migrations are not
required by default.

The default strategy for development-era persisted state may be direct
relocation, discard and rebuild, or another explicitly documented breaking
strategy.

Compatibility migrations may still be added when they are cheap, reduce risk, or
help local development, but they are not required unless a feature scope
explicitly calls for them.

## Future Compatibility Period

When ComCross enters a stable compatibility period, this policy must be updated
before changing the default rules.

At that point, directory moves, persisted schema changes, plugin layout changes,
and install layout changes should require an explicit compatibility or migration
strategy unless the project owner decides otherwise.
