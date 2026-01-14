#!/usr/bin/env bash
set -euo pipefail

version=""
config="Release"
output_dir="artifacts"
rid="linux-x64"
prefix="/opt/comcross"
runtime_dep="dotnet-runtime-8.0"

usage() {
  cat <<'EOF'
Usage: scripts/package-linux.sh -v VERSION [-c CONFIG] [-o OUTPUT_DIR] [-r RID] [-p PREFIX]

Builds DEB and RPM packages from framework-dependent publish output.

Options:
  -v, --version      Package version (required)
  -c, --config       Build configuration. Default: Release
  -o, --output       Output directory. Default: artifacts
  -r, --rid          RID to package. Default: linux-x64
  -p, --prefix       Install prefix. Default: /opt/comcross
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -v|--version)
      version="${2:-}"
      shift 2
      ;;
    -c|--config)
      config="${2:-}"
      shift 2
      ;;
    -o|--output)
      output_dir="${2:-}"
      shift 2
      ;;
    -r|--rid)
      rid="${2:-}"
      shift 2
      ;;
    -p|--prefix)
      prefix="${2:-}"
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
  echo "Version is required." >&2
  usage >&2
  exit 1
fi

if ! command -v fpm >/dev/null 2>&1; then
  echo "fpm is required to build DEB/RPM packages." >&2
  exit 1
fi

source_dir="${output_dir}/framework-dependent/ComCross-${rid}-${config}"
if [[ ! -d "${source_dir}" ]]; then
  echo "Publish output not found: ${source_dir}" >&2
  exit 1
fi

pkg_dir="${output_dir}/packages"
mkdir -p "${pkg_dir}"

# Determine architecture for package naming
arch="amd64"
if [[ "${rid}" == "linux-arm64" ]]; then
  arch="arm64"
fi

# Prepare staging area for additional files
staging_dir="/tmp/comcross-staging-$$"
rm -rf "${staging_dir}"
mkdir -p "${staging_dir}${prefix}"
mkdir -p "${staging_dir}/usr/share/applications"
mkdir -p "${staging_dir}/usr/share/icons/hicolor/256x256/apps"

# Copy application files
cp -r "${source_dir}"/* "${staging_dir}${prefix}/"

# Copy desktop file and icon
cp "src/Assets/Resources/comcross.desktop" "${staging_dir}/usr/share/applications/"
cp "src/Assets/Resources/Icons/app-icon-256.png" "${staging_dir}/usr/share/icons/hicolor/256x256/apps/comcross.png"

fpm -s dir -t deb -n comcross -v "${version}" \
  -a "${arch}" \
  -d "${runtime_dep}" \
  --deb-no-default-config-files \
  -C "${staging_dir}" \
  -p "${pkg_dir}" \
  .

rpm_arch="x86_64"
if [[ "${rid}" == "linux-arm64" ]]; then
  rpm_arch="aarch64"
fi

fpm -s dir -t rpm -n comcross -v "${version}" \
  -a "${rpm_arch}" \
  -d "${runtime_dep}" \
  -C "${staging_dir}" \
  -p "${pkg_dir}" \
  .

# Clean up staging directory
rm -rf "${staging_dir}"
