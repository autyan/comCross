#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/release/common.sh
source "${script_dir}/common.sh"

repo_root="$(release_repo_root)"
cd "${repo_root}"

version=""
config="Release"
output_dir="artifacts/release"
rids="$(release_default_rids)"
tool_dir="artifacts/release/tools/appimage"

usage() {
  cat <<'EOF'
Usage: scripts/release/build-appimages.sh --version VERSION [options]

Builds self-contained AppImage packages for Linux release fallback users.

Options:
  --version VERSION      App version, with or without leading v.
  --config CONFIG        Build configuration. Default: Release.
  --output DIR           Release output directory. Default: artifacts/release.
  --rids RID_LIST        Comma-separated Linux RIDs. Default: linux-x64,linux-arm64.
  --tool-dir DIR         Directory for downloaded appimagetool/runtime files.
  -h, --help             Show help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="${2:-}"
      shift 2
      ;;
    --config)
      config="${2:-}"
      shift 2
      ;;
    --output)
      output_dir="${2:-}"
      shift 2
      ;;
    --rids)
      rids="$(release_split_csv "${2:-}")"
      shift 2
      ;;
    --tool-dir)
      tool_dir="${2:-}"
      shift 2
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 1
      ;;
  esac
done

if [[ -z "${version}" ]]; then
  echo "--version is required." >&2
  usage >&2
  exit 1
fi

version="$(release_normalize_version "${version}")"
release_require_command curl

mkdir -p "${tool_dir}"
appimagetool="${tool_dir}/appimagetool-x86_64.AppImage"
if [[ ! -x "${appimagetool}" ]]; then
  curl -L \
    -o "${appimagetool}" \
    "https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage"
  chmod +x "${appimagetool}"
fi

package_dir="${output_dir}/packages/linux"
mkdir -p "${package_dir}"

for rid in ${rids}; do
  case "${rid}" in
    linux-*) ;;
    *) echo "Unsupported AppImage RID: ${rid}" >&2; exit 1 ;;
  esac

  source_dir="${output_dir}/self-contained/ComCross-${rid}-${config}"
  if [[ ! -d "${source_dir}" ]]; then
    echo "Publish output not found: ${source_dir}" >&2
    echo "Run scripts/release/build-publish-output.sh first." >&2
    exit 1
  fi

  arch="$(release_appimage_arch "${rid}")"
  appdir="${output_dir}/appimage/ComCross-${rid}.AppDir"
  rm -rf "${appdir}"
  mkdir -p "${appdir}/usr/lib/comcross"
  mkdir -p "${appdir}/usr/share/applications"
  mkdir -p "${appdir}/usr/share/icons/hicolor/256x256/apps"

  cp -a "${source_dir}/." "${appdir}/usr/lib/comcross/"
  chmod +x "${appdir}/usr/lib/comcross/ComCross.Shell" || true

  cat > "${appdir}/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "${here}/usr/lib/comcross/ComCross.Shell" "$@"
EOF
  chmod +x "${appdir}/AppRun"

  sed 's#^Exec=.*#Exec=ComCross#' \
    src/Assets/Resources/comcross.desktop \
    > "${appdir}/comcross.desktop"
  cp "${appdir}/comcross.desktop" "${appdir}/usr/share/applications/comcross.desktop"
  cp src/Assets/Resources/Icons/app-icon-256.png "${appdir}/comcross.png"
  cp src/Assets/Resources/Icons/app-icon-256.png \
    "${appdir}/usr/share/icons/hicolor/256x256/apps/comcross.png"

  output_file="${package_dir}/ComCross-${version}-${rid}.AppImage"
  rm -f "${output_file}"

  if [[ "${arch}" == "x86_64" ]]; then
    APPIMAGE_EXTRACT_AND_RUN=1 ARCH="${arch}" "${appimagetool}" "${appdir}" "${output_file}"
  else
    runtime_file="${tool_dir}/runtime-${arch}"
    if [[ ! -f "${runtime_file}" ]]; then
      curl -L \
        -o "${runtime_file}" \
        "https://github.com/AppImage/AppImageKit/releases/download/continuous/runtime-${arch}"
      chmod +x "${runtime_file}"
    fi
    APPIMAGE_EXTRACT_AND_RUN=1 ARCH="${arch}" "${appimagetool}" --runtime-file "${runtime_file}" "${appdir}" "${output_file}"
  fi
done

printf 'AppImages ready under %s\n' "${package_dir}"
