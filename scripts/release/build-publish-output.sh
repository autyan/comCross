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
include_windows=false

usage() {
  cat <<'EOF'
Usage: scripts/release/build-publish-output.sh --version VERSION [options]

Builds release publish outputs without creating portable zip/tar.gz archives.

Options:
  --version VERSION      Release version, with or without leading v.
  --config CONFIG        Build configuration. Default: Release.
  --output DIR           Output directory. Default: artifacts/release.
  --rids RID_LIST        Comma-separated RIDs. Default: linux-x64,linux-arm64.
  --include-windows      Include win-x64 and win-arm64 publish outputs.
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
    --include-windows)
      include_windows=true
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

if [[ "${include_windows}" == "true" ]]; then
  rids="${rids} win-x64 win-arm64"
fi

scripts/package-release.sh \
  -c "${config}" \
  -o "${output_dir}" \
  -r "$(printf '%s' "${rids}" | tr ' ' ',')" \
  --no-package

printf 'Release publish output ready for %s under %s\n' "${version}" "${output_dir}"
