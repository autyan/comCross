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
channel="Stable"
directory_name=""
instance_id=""
schema_line="v0"
plugin_signing_key=""
plugin_signing_key_id="comcross-plugin-official-2026"
require_plugin_signing=false

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
  --channel CHANNEL      Instance channel: Stable, Dev, or EAP. Default: Stable.
  --directory-name NAME  Override instance directoryName.
  --instance-id ID       Override instance id.
  --schema-line VALUE    Instance schema line. Default: v0.
  --plugin-signing-key FILE
                          PEM private key used to sign bundled official plugins.
  --plugin-signing-key-id ID
                          Plugin signing key id. Default: comcross-plugin-official-2026.
  --require-plugin-signing
                          Fail if plugin signing key is missing.
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
    --channel)
      channel="${2:-}"
      shift 2
      ;;
    --directory-name)
      directory_name="${2:-}"
      shift 2
      ;;
    --instance-id)
      instance_id="${2:-}"
      shift 2
      ;;
    --schema-line)
      schema_line="${2:-}"
      shift 2
      ;;
    --plugin-signing-key)
      plugin_signing_key="${2:-}"
      shift 2
      ;;
    --plugin-signing-key-id)
      plugin_signing_key_id="${2:-}"
      shift 2
      ;;
    --require-plugin-signing)
      require_plugin_signing=true
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

package_args=()
if [[ "${include_windows}" == "true" ]]; then
  rids="${rids} win-x64 win-arm64"
fi
if [[ -n "${directory_name}" ]]; then
  package_args+=(--directory-name "${directory_name}")
fi
if [[ -n "${instance_id}" ]]; then
  package_args+=(--instance-id "${instance_id}")
fi
if [[ -n "${plugin_signing_key}" ]]; then
  package_args+=(--plugin-signing-key "${plugin_signing_key}")
fi
if [[ "${require_plugin_signing}" == "true" ]]; then
  package_args+=(--require-plugin-signing)
fi

scripts/package-release.sh \
  -c "${config}" \
  -o "${output_dir}" \
  -r "$(printf '%s' "${rids}" | tr ' ' ',')" \
  --channel "${channel}" \
  --schema-line "${schema_line}" \
  --plugin-signing-key-id "${plugin_signing_key_id}" \
  --no-package \
  "${package_args[@]}"

printf 'Release publish output ready for %s under %s\n' "${version}" "${output_dir}"
