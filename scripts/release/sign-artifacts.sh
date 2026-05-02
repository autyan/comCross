#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/release/common.sh
source "${script_dir}/common.sh"

repo_root="$(release_repo_root)"
cd "${repo_root}"

package_dir="artifacts/release/packages"
checksum_file=""
gpg_private_key=""
gpg_passphrase="${COMCROSS_GPG_PASSPHRASE:-}"
gpg_key_id="${COMCROSS_GPG_KEY_ID:-}"
require_signing=false

usage() {
  cat <<'EOF'
Usage: scripts/release/sign-artifacts.sh [options]

Signs SHA256SUMS with GPG. Local verification reads a private key from a path.
GitHub Actions should pass key material through secrets and temporary files.

Options:
  --package-dir DIR        Package directory. Default: artifacts/release/packages.
  --checksum-file FILE     Checksum file. Default: <package-dir>/SHA256SUMS.
  --gpg-private-key FILE   ASCII-armored private key path for local verification.
  --gpg-passphrase VALUE   GPG passphrase. Defaults to COMCROSS_GPG_PASSPHRASE.
  --gpg-key-id VALUE       Signing key id. Defaults to COMCROSS_GPG_KEY_ID.
  --require-signing        Fail when signing inputs are missing.
  -h, --help               Show help.
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package-dir)
      package_dir="${2:-}"
      shift 2
      ;;
    --checksum-file)
      checksum_file="${2:-}"
      shift 2
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

if [[ -z "${checksum_file}" ]]; then
  checksum_file="${package_dir}/SHA256SUMS"
fi

if [[ ! -f "${checksum_file}" ]]; then
  echo "Checksum file not found: ${checksum_file}" >&2
  exit 1
fi

if [[ -z "${gpg_private_key}" ]]; then
  if [[ "${require_signing}" == "true" ]]; then
    echo "Missing --gpg-private-key while signing is required." >&2
    exit 1
  fi
  echo "No GPG private key provided; signing skipped."
  exit 0
fi

if [[ ! -f "${gpg_private_key}" ]]; then
  echo "GPG private key not found: ${gpg_private_key}" >&2
  exit 1
fi

release_require_command gpg

gpg_home="$(mktemp -d)"
cleanup() {
  rm -rf "${gpg_home}"
}
trap cleanup EXIT

chmod 700 "${gpg_home}"
gpg --batch --homedir "${gpg_home}" --import "${gpg_private_key}" >/dev/null

sign_args=(--batch --yes --armor --detach-sign --homedir "${gpg_home}")
if [[ -n "${gpg_key_id}" ]]; then
  sign_args+=(--local-user "${gpg_key_id}")
fi
if [[ -n "${gpg_passphrase}" ]]; then
  sign_args+=(--pinentry-mode loopback --passphrase "${gpg_passphrase}")
fi

gpg "${sign_args[@]}" -o "${checksum_file}.asc" "${checksum_file}"
printf 'Signature written to %s\n' "${checksum_file}.asc"
