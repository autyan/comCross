# ComCross Website

This folder contains the first official ComCross website. It is intentionally a
zero-dependency static site so it can be hosted from GitHub Pages or any static
file host without adding a frontend toolchain to the repository.

## Local Preview

Open `website/index.html` directly in a browser, or serve the repository root
with a simple static server:

```bash
python3 -m http.server 8080
```

Then open:

```text
http://localhost:8080/website/
```

## Hosting

Recommended first hosting target: GitHub Pages.

Suggested setup:

- Source: GitHub Actions.
- Publish artifact: `website/`.
- Custom domain: optional after the first public version is accepted.

The website links to repository Markdown documents with absolute GitHub URLs so
the published GitHub Pages artifact can contain only the static website files.

## Release Download Manifest

The download section is driven by:

```text
website/releases/latest.json
```

Update this file after each package release. Keep `version`, `releaseUrl`,
`checksumsUrl`, and every asset URL aligned with the GitHub Release assets.
The static JavaScript reads this file, detects the visitor's likely OS and CPU
architecture, and selects a direct download link without requiring a GitHub
Release page hop.

Linux distribution detection is intentionally conservative. Linux visitors get
AppImage by default and can choose DEB or RPM from the package dropdown.

## Screenshots

Screenshots are stored under `website/assets/screenshots/`.

Current screenshots:

- `comcross-udp-listener.png`: primary homepage product image.
- `comcross-quick-create.png`: quick session creation and adapter setup.
- `comcross-session-details.png`: session details and adapter state.

When replacing screenshots, keep file names stable unless the HTML is updated in
the same change. Use sanitized local-loopback examples and avoid real device
names, hostnames, paths, or private payloads.

## Icon

`website/assets/comcross-icon.png` mirrors the Shell window icon family. Keep it
aligned with the application icon used by `src/Shell/app-icon.ico`.

## Content Boundaries

The website is a public project entry point, not an additional source of release
truth. Release, compatibility, signing, and implementation claims must stay
aligned with the English source documents under the repository root and `docs/`.
