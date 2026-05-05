const year = document.getElementById("year");

if (year) {
  year.textContent = String(new Date().getFullYear());
}

const downloadState = {
  manifest: createFallbackReleaseManifest(),
  os: "windows",
  arch: "x64",
  format: "msi"
};

const osSelect = document.getElementById("download-os");
const archSelect = document.getElementById("download-arch");
const formatSelect = document.getElementById("download-format");
const downloadButton = document.getElementById("download-button");
const downloadTitle = document.getElementById("download-title");
const downloadSummary = document.getElementById("download-summary");
const checksumsLink = document.getElementById("checksums-link");
const allDownloadsLink = document.getElementById("all-downloads-link");

if (osSelect && archSelect && formatSelect && downloadButton) {
  initializeDownloads();
}

async function initializeDownloads() {
  applyDetectedPlatform();
  syncDownloadControls();

  try {
    const response = await fetch("releases/latest.json", { cache: "no-store" });
    if (!response.ok) {
      throw new Error(`Release manifest request failed: ${response.status}`);
    }

    downloadState.manifest = await response.json();
    syncDownloadControls();
  } catch {
    downloadSummary.textContent = `${downloadState.manifest.packageVersionLabel}. Direct download from GitHub Releases.`;
  }

  osSelect.addEventListener("change", () => {
    downloadState.os = osSelect.value;
    chooseDefaultFormat();
    syncDownloadControls();
  });

  archSelect.addEventListener("change", () => {
    downloadState.arch = archSelect.value;
    syncDownloadControls();
  });

  formatSelect.addEventListener("change", () => {
    downloadState.format = formatSelect.value;
    syncDownloadControls();
  });
}

function applyDetectedPlatform() {
  const platform = [
    navigator.userAgentData?.platform,
    navigator.platform,
    navigator.userAgent
  ].filter(Boolean).join(" ").toLowerCase();

  if (platform.includes("linux")) {
    downloadState.os = "linux";
    downloadState.format = "appimage";
  } else if (platform.includes("win")) {
    downloadState.os = "windows";
    downloadState.format = "msi";
  }

  if (platform.includes("arm64") || platform.includes("aarch64")) {
    downloadState.arch = "arm64";
  }
}

function chooseDefaultFormat() {
  downloadState.format = downloadState.os === "linux" ? "appimage" : "msi";
}

function syncDownloadControls() {
  const manifest = downloadState.manifest;
  if (!manifest) {
    return;
  }

  const assets = Array.isArray(manifest.assets) ? manifest.assets : [];
  osSelect.value = downloadState.os;
  archSelect.value = downloadState.arch;

  const formats = unique(assets
    .filter(item => item.os === downloadState.os && item.arch === downloadState.arch)
    .map(item => item.format));

  formatSelect.replaceChildren(...formats.map(format => {
    const option = document.createElement("option");
    option.value = format;
    option.textContent = formatLabel(format);
    return option;
  }));

  if (!formats.includes(downloadState.format)) {
    downloadState.format = formats[0] ?? "";
  }

  formatSelect.value = downloadState.format;

  const selected = assets.find(item =>
    item.os === downloadState.os
    && item.arch === downloadState.arch
    && item.format === downloadState.format);

  downloadTitle.textContent = selected?.label ?? manifest.packageVersionLabel ?? "ComCross packages";
  downloadSummary.textContent = selected
    ? `${manifest.packageVersionLabel}. Direct download from GitHub Releases.`
    : "No package is currently listed for this selection.";

  downloadButton.href = selected?.url ?? manifest.releaseUrl;
  downloadButton.textContent = selected ? `Download ${selected.label}` : "Open Releases";
  downloadButton.toggleAttribute("aria-disabled", !selected);

  checksumsLink.href = manifest.checksumsUrl ?? manifest.releaseUrl;
  allDownloadsLink.href = manifest.releaseUrl;
}

function unique(values) {
  return [...new Set(values.filter(Boolean))];
}

function formatLabel(format) {
  return {
    appimage: "AppImage",
    deb: "DEB",
    rpm: "RPM",
    msi: "MSI"
  }[format] ?? format.toUpperCase();
}

function createFallbackReleaseManifest() {
  const version = "0.6.0";
  const tag = `v${version}`;
  const baseUrl = `https://github.com/autyan/comCross/releases/download/${tag}`;

  return {
    version,
    packageVersionLabel: `Package release ${tag}`,
    releaseUrl: `https://github.com/autyan/comCross/releases/tag/${tag}`,
    checksumsUrl: `${baseUrl}/SHA256SUMS`,
    assets: [
      {
        id: "windows-x64-msi",
        os: "windows",
        arch: "x64",
        format: "msi",
        label: "Windows x64 MSI",
        url: `${baseUrl}/ComCross-${version}-win-x64.msi`
      },
      {
        id: "windows-arm64-msi",
        os: "windows",
        arch: "arm64",
        format: "msi",
        label: "Windows ARM64 MSI",
        url: `${baseUrl}/ComCross-${version}-win-arm64.msi`
      },
      {
        id: "linux-x64-appimage",
        os: "linux",
        arch: "x64",
        format: "appimage",
        label: "Linux x64 AppImage",
        url: `${baseUrl}/ComCross-${version}-linux-x64.AppImage`
      },
      {
        id: "linux-arm64-appimage",
        os: "linux",
        arch: "arm64",
        format: "appimage",
        label: "Linux ARM64 AppImage",
        url: `${baseUrl}/ComCross-${version}-linux-arm64.AppImage`
      },
      {
        id: "linux-x64-deb",
        os: "linux",
        arch: "x64",
        format: "deb",
        label: "Linux x64 DEB",
        url: `${baseUrl}/comcross_${version}_amd64.deb`
      },
      {
        id: "linux-arm64-deb",
        os: "linux",
        arch: "arm64",
        format: "deb",
        label: "Linux ARM64 DEB",
        url: `${baseUrl}/comcross_${version}_arm64.deb`
      },
      {
        id: "linux-x64-rpm",
        os: "linux",
        arch: "x64",
        format: "rpm",
        label: "Linux x64 RPM",
        url: `${baseUrl}/comcross-${version}-1.x86_64.rpm`
      },
      {
        id: "linux-arm64-rpm",
        os: "linux",
        arch: "arm64",
        format: "rpm",
        label: "Linux ARM64 RPM",
        url: `${baseUrl}/comcross-${version}-1.aarch64.rpm`
      }
    ]
  };
}
