#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/release/common.sh
source "${script_dir}/common.sh"

repo_root="$(release_repo_root)"
cd "${repo_root}"

package_dir="artifacts/release/packages"
output_file=""

usage() {
  cat <<'EOF'
Usage: scripts/release/generate-checksums.sh [options]

Generates SHA256SUMS for release package files.

Options:
  --package-dir DIR      Package directory. Default: artifacts/release/packages.
  --output FILE          Checksum file. Default: <package-dir>/SHA256SUMS.
  -h, --help             Show help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-dir)
      package_dir="${2:-}"
      shift 2
      ;;
    --output)
      output_file="${2:-}"
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

if [[ -z "${output_file}" ]]; then
  output_file="${package_dir}/SHA256SUMS"
fi

if [[ ! -d "${package_dir}" ]]; then
  echo "Package directory not found: ${package_dir}" >&2
  exit 1
fi

mkdir -p "$(dirname "${output_file}")"
tmp_file="$(mktemp)"
find "${package_dir}" -type f \
  ! -name 'SHA256SUMS' \
  ! -name 'SHA256SUMS.asc' \
  -print0 \
  | sort -z \
  | xargs -0 sha256sum \
  | sed "s#  ${package_dir}/#  #" \
  > "${tmp_file}"
mv "${tmp_file}" "${output_file}"

printf 'Checksums written to %s\n' "${output_file}"
