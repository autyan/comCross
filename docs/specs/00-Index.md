# Technical Specifications Index

This directory contains the public technical specifications for ComCross v0.4.0.

## Product And Architecture

- [01-Compatibility-Policy.md](01-Compatibility-Policy.md) - Current compatibility stage and breaking-change policy
- [02-MVP-Scope.md](02-MVP-Scope.md) - v0.4 release scope and non-goals
- [03-System-Architecture.md](03-System-Architecture.md) - Current module boundaries and runtime architecture
- [04-Plugin-System.md](04-Plugin-System.md) - Plugin, host, IPC, UI-state, and bus capability model
- [05-Workspace-State.md](05-Workspace-State.md) - Workspace, workload, session, and persistence semantics

## User Experience

- [06-UI-UX-Spec.md](06-UI-UX-Spec.md) - User interface and experience specifications

## Development Guides

- [10-Plugin-Development-Guide.md](10-Plugin-Development-Guide.md) - Guide for developing plugins
- [11-Packaging-Guide.md](11-Packaging-Guide.md) - Application packaging and distribution guide

## Compatibility Note

v0.4.0 intentionally does not migrate old v0.3 session state. Users of earlier development builds should recreate sessions after upgrading.

**Last Updated**: 2026-05-02
