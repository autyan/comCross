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
skip_publish=false
skip_linux_packages=false
skip_appimage=false
gpg_private_key=""
gpg_passphrase="${COMCROSS_GPG_PASSPHRASE:-}"
gpg_key_id="${COMCROSS_GPG_KEY_ID:-}"
require_signing=false

usage() {
  cat <<'EOF'
Usage: scripts/release/local-verify.sh --version VERSION [options]

Runs the local release packaging flow up to Linux installable packages,
AppImage fallback packages, checksums, and optional GPG signing.

Options:
  --version VERSION           Release version, with or without leading v.
  --config CONFIG             Build configuration. Default: Release.
  --output DIR                Release output directory. Default: artifacts/release.
  --rids RID_LIST             Comma-separated Linux RIDs. Default: linux-x64,linux-arm64.
  --skip-publish              Reuse existing publish outputs.
  --skip-linux-packages       Skip DEB/RPM generation.
  --skip-appimage             Skip AppImage generation.
  --gpg-private-key FILE      Local ASCII-armored GPG private key path.
  --gpg-passphrase VALUE      GPG passphrase. Defaults to COMCROSS_GPG_PASSPHRASE.
  --gpg-key-id VALUE          Signing key id. Defaults to COMCROSS_GPG_KEY_ID.
  --require-signing           Fail when signing inputs are missing.
  -h, --help                  Show help.
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
    --skip-publish)
      skip_publish=true
      shift
      ;;
    --skip-linux-packages)
      skip_linux_packages=true
      shift
      ;;
    --skip-appimage)
      skip_appimage=true
      shift
      ;;
    --gpg-private-key)
      gpg_private_key="${2:-}"
      shift 2
      ;;
    --gpg-passphrase)
      gpg_passphrase="${2:-}"
      shift 2
      ;;
    --gpg-key-id)
      gpg_key_id="${2:-}"
      shift 2
      ;;
    --require-signing)
      require_signing=true
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
rid_csv="$(printf '%s' "${rids}" | tr ' ' ',')"

if [[ "${skip_publish}" != "true" ]]; then
  scripts/release/build-publish-output.sh \
    --version "${version}" \
    --config "${config}" \
    --output "${output_dir}" \
    --rids "${rid_csv}"
fi

if [[ "${skip_linux_packages}" != "true" ]]; then
  scripts/release/build-linux-packages.sh \
    --version "${version}" \
    --config "${config}" \
    --output "${output_dir}" \
    --rids "${rid_csv}"
fi

if [[ "${skip_appimage}" != "true" ]]; then
  scripts/release/build-appimages.sh \
    --version "${version}" \
    --config "${config}" \
    --output "${output_dir}" \
    --rids "${rid_csv}"
fi

scripts/release/generate-checksums.sh \
  --package-dir "${output_dir}/packages"

sign_args=(--package-dir "${output_dir}/packages")
if [[ -n "${gpg_private_key}" ]]; then
  sign_args+=(--gpg-private-key "${gpg_private_key}")
fi
if [[ -n "${gpg_passphrase}" ]]; then
  sign_args+=(--gpg-passphrase "${gpg_passphrase}")
fi
if [[ -n "${gpg_key_id}" ]]; then
  sign_args+=(--gpg-key-id "${gpg_key_id}")
fi
if [[ "${require_signing}" == "true" ]]; then
  sign_args+=(--require-signing)
fi

scripts/release/sign-artifacts.sh "${sign_args[@]}"

printf 'Local release verification complete for %s.\n' "${version}"
