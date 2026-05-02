#!/usr/bin/env bash
set -euo pipefail

release_repo_root() {
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  cd "${script_dir}/../.." >/dev/null 2>&1
  pwd
}

release_require_command() {
  local command_name="$1"
  if ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "Required command not found: ${command_name}" >&2
    return 1
  fi
}

release_normalize_version() {
  local version="$1"
  version="${version#v}"
  if [[ ! "${version}" =~ ^[0-9]+(\.[0-9]+){1,3}([._+-][A-Za-z0-9]+)?$ ]]; then
    echo "Invalid release version: ${version}" >&2
    return 1
  fi
  printf '%s\n' "${version}"
}

release_default_rids() {
  printf '%s\n' "linux-x64 linux-arm64"
}

release_deb_arch() {
  case "$1" in
    linux-x64) printf '%s\n' "amd64" ;;
    linux-arm64) printf '%s\n' "arm64" ;;
    *) echo "Unsupported Linux RID for DEB: $1" >&2; return 1 ;;
  esac
}

release_rpm_arch() {
  case "$1" in
    linux-x64) printf '%s\n' "x86_64" ;;
    linux-arm64) printf '%s\n' "aarch64" ;;
    *) echo "Unsupported Linux RID for RPM: $1" >&2; return 1 ;;
  esac
}

release_appimage_arch() {
  case "$1" in
    linux-x64) printf '%s\n' "x86_64" ;;
    linux-arm64) printf '%s\n' "aarch64" ;;
    *) echo "Unsupported Linux RID for AppImage: $1" >&2; return 1 ;;
  esac
}

release_split_csv() {
  local input="$1"
  printf '%s' "${input//,/ }"
}

release_channel_defaults() {
  local channel="$1"
  case "${channel}" in
    Stable) printf '%s\n' "ComCross comcross-stable" ;;
    Dev) printf '%s\n' "ComCrossDev comcross-dev" ;;
    EAP) printf '%s\n' "ComCrossEAP comcross-eap" ;;
    *) echo "Unsupported release channel: ${channel}" >&2; return 1 ;;
  esac
}
