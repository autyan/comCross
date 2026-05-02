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
channel="Stable"
directory_name=""
instance_id=""
schema_line="v0"
plugin_signing_key=""
plugin_signing_key_id="comcross-plugin-official-2026"
require_plugin_signing=false

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
  --channel CHANNEL           Instance channel: Stable, Dev, or EAP. Default: Stable.
  --directory-name NAME       Override instance directoryName.
  --instance-id ID            Override instance id.
  --schema-line VALUE         Instance schema line. Default: v0.
  --plugin-signing-key FILE   PEM private key used to sign bundled official plugins.
  --plugin-signing-key-id ID  Plugin signing key id. Default: comcross-plugin-official-2026.
  --require-plugin-signing    Fail if plugin signing key is missing.
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
rid_csv="$(printf '%s' "${rids}" | tr ' ' ',')"

publish_args=(
  --version "${version}"
  --config "${config}"
  --output "${output_dir}"
  --rids "${rid_csv}"
  --channel "${channel}"
  --schema-line "${schema_line}"
  --plugin-signing-key-id "${plugin_signing_key_id}"
)
if [[ -n "${directory_name}" ]]; then
  publish_args+=(--directory-name "${directory_name}")
fi
if [[ -n "${instance_id}" ]]; then
  publish_args+=(--instance-id "${instance_id}")
fi
if [[ -n "${plugin_signing_key}" ]]; then
  publish_args+=(--plugin-signing-key "${plugin_signing_key}")
fi
if [[ "${require_plugin_signing}" == "true" ]]; then
  publish_args+=(--require-plugin-signing)
fi

if [[ "${skip_publish}" != "true" ]]; then
  scripts/release/build-publish-output.sh "${publish_args[@]}"
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

for rid in ${rids}; do
  for mode in framework-dependent self-contained; do
    publish_dir="${output_dir}/${mode}/ComCross-${rid}-${config}"
    [[ -d "${publish_dir}" ]] || continue
    for required in ComCross.Startup ComCross.Shell ComCross.Instance.json bundled-plugins; do
      if [[ ! -e "${publish_dir}/${required}" ]]; then
        echo "Release verification failed: missing ${required} in ${publish_dir}" >&2
        exit 1
      fi
    done
  done
done

if [[ ! -f "${output_dir}/packages/SHA256SUMS" ]]; then
  echo "Release verification failed: missing ${output_dir}/packages/SHA256SUMS" >&2
  exit 1
fi

printf 'Local release verification complete for %s.\n' "${version}"
