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
image_name="comcross-linux-packager:local"
runtime_dep="dotnet-runtime-8.0"
inside=false

usage() {
  cat <<'EOF'
Usage: scripts/release/build-linux-packages.sh --version VERSION [options]

Builds DEB and RPM packages from framework-dependent Linux publish outputs.
The default host mode builds and runs a local Docker image that carries fpm.

Options:
  --version VERSION      Package version, with or without leading v.
  --config CONFIG        Build configuration. Default: Release.
  --output DIR           Release output directory. Default: artifacts/release.
  --rids RID_LIST        Comma-separated Linux RIDs. Default: linux-x64,linux-arm64.
  --image NAME           Local Docker image name. Default: comcross-linux-packager:local.
  --runtime-dep NAME     Package runtime dependency. Default: dotnet-runtime-8.0.
  --inside-container     Internal mode used by the packager container.
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
    --image)
      image_name="${2:-}"
      shift 2
      ;;
    --runtime-dep)
      runtime_dep="${2:-}"
      shift 2
      ;;
    --inside-container)
      inside=true
      shift
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

if [[ "${inside}" != "true" ]]; then
  release_require_command docker
  docker build -t "${image_name}" -f scripts/release/linux-packager.Dockerfile .
  docker run --rm \
    --user "$(id -u):$(id -g)" \
    -e HOME=/tmp \
    -v "${repo_root}:/repo:Z" \
    -w /repo \
    "${image_name}" \
    scripts/release/build-linux-packages.sh \
      --inside-container \
      --version "${version}" \
      --config "${config}" \
      --output "${output_dir}" \
      --rids "$(printf '%s' "${rids}" | tr ' ' ',')" \
      --runtime-dep "${runtime_dep}"
  exit 0
fi

release_require_command fpm

package_dir="${output_dir}/packages/linux"
mkdir -p "${package_dir}"

for rid in ${rids}; do
  case "${rid}" in
    linux-*) ;;
    *) echo "Unsupported Linux package RID: ${rid}" >&2; exit 1 ;;
  esac

  source_dir="${output_dir}/framework-dependent/ComCross-${rid}-${config}"
  if [[ ! -d "${source_dir}" ]]; then
    echo "Publish output not found: ${source_dir}" >&2
    echo "Run scripts/release/build-publish-output.sh first." >&2
    exit 1
  fi

  deb_arch="$(release_deb_arch "${rid}")"
  rpm_arch="$(release_rpm_arch "${rid}")"
  staging_dir="$(mktemp -d)"
  trap 'rm -rf "${staging_dir}"' EXIT

  mkdir -p "${staging_dir}/opt/comcross"
  mkdir -p "${staging_dir}/usr/bin"
  mkdir -p "${staging_dir}/usr/share/applications"
  mkdir -p "${staging_dir}/usr/share/icons/hicolor/256x256/apps"

  cp -a "${source_dir}/." "${staging_dir}/opt/comcross/"
  chmod +x "${staging_dir}/opt/comcross/ComCross.Shell" || true

  cat > "${staging_dir}/usr/bin/comcross" <<'EOF'
#!/usr/bin/env bash
exec /opt/comcross/ComCross.Shell "$@"
EOF
  chmod +x "${staging_dir}/usr/bin/comcross"

  sed 's#^Exec=.*#Exec=/opt/comcross/ComCross.Shell#' \
    src/Assets/Resources/comcross.desktop \
    > "${staging_dir}/usr/share/applications/comcross.desktop"
  cp src/Assets/Resources/Icons/app-icon-256.png \
    "${staging_dir}/usr/share/icons/hicolor/256x256/apps/comcross.png"

  fpm -s dir -t deb \
    -n comcross \
    -v "${version}" \
    -a "${deb_arch}" \
    -d "${runtime_dep}" \
    --license MIT \
    --url "https://github.com/autyan/comCross" \
    --maintainer "autyan" \
    --description "Cross-platform embedded communication toolbox" \
    --deb-no-default-config-files \
    -C "${staging_dir}" \
    -p "${package_dir}" \
    .

  fpm -s dir -t rpm \
    -n comcross \
    -v "${version}" \
    -a "${rpm_arch}" \
    -d "${runtime_dep}" \
    --license MIT \
    --url "https://github.com/autyan/comCross" \
    --maintainer "autyan" \
    --description "Cross-platform embedded communication toolbox" \
    -C "${staging_dir}" \
    -p "${package_dir}" \
    .

  rm -rf "${staging_dir}"
  trap - EXIT
done

printf 'Linux packages ready under %s\n' "${package_dir}"
