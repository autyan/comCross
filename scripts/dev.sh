#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "${script_dir}/.." && pwd)"
cd "${repo_root}"

command_name="${1:-}"
if [[ $# -gt 0 ]]; then
  shift
fi

config="Release"
version="0.6.1-dev"
output_dir="artifacts/dev"
channel="Dev"
rid=""

detect_default_rid() {
  local os
  local arch

  os="$(uname -s)"
  arch="$(uname -m)"

  case "${os}" in
    Linux)
      case "${arch}" in
        x86_64|amd64) printf '%s\n' "linux-x64" ;;
        aarch64|arm64) printf '%s\n' "linux-arm64" ;;
        *) echo "Unsupported Linux architecture: ${arch}" >&2; return 1 ;;
      esac
      ;;
    *)
      echo "Unsupported OS for scripts/dev.sh startup: ${os}" >&2
      return 1
      ;;
  esac
}

usage() {
  cat <<'EOF'
Usage: scripts/dev.sh COMMAND [options]

Commands:
  build      Build the solution.
  publish    Publish a local Dev instance.
  startup    Publish if needed, then launch the Dev instance through Startup.

Options:
  --config CONFIG     Build configuration. Default: Release.
  --version VERSION   Dev package version label. Default: 0.6.1-dev.
  --output DIR        Publish output directory. Default: artifacts/dev.
  --rid RID           Runtime identifier. Default: detected host RID.
  --channel CHANNEL   Instance channel. Default: Dev.
  -h, --help          Show help.

Examples:
  scripts/dev.sh build
  scripts/dev.sh publish
  scripts/dev.sh startup
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --config)
      config="${2:-}"
      shift 2
      ;;
    --version)
      version="${2:-}"
      shift 2
      ;;
    --output)
      output_dir="${2:-}"
      shift 2
      ;;
    --rid)
      rid="${2:-}"
      shift 2
      ;;
    --channel)
      channel="${2:-}"
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

if [[ -z "${command_name}" || "${command_name}" == "-h" || "${command_name}" == "--help" ]]; then
  usage
  exit 0
fi

if [[ -z "${config}" ]]; then
  echo "--config cannot be empty." >&2
  exit 1
fi

if [[ -z "${rid}" ]]; then
  rid="$(detect_default_rid)"
fi

publish_dir="${output_dir}/framework-dependent/ComCross-${rid}-${config}"
startup_exe="${publish_dir}/ComCross.Startup"

run_build() {
  dotnet build ComCross.sln -c "${config}"
}

run_publish() {
  scripts/release/build-publish-output.sh \
    --version "${version}" \
    --config "${config}" \
    --output "${output_dir}" \
    --rids "${rid}" \
    --channel "${channel}"
}

run_startup() {
  if [[ ! -x "${startup_exe}" ]]; then
    run_publish
  fi

  printf 'Starting %s instance from %s\n' "${channel}" "${startup_exe}"
  "${startup_exe}" >/dev/null 2>&1 &
}

case "${command_name}" in
  build)
    run_build
    ;;
  publish)
    run_publish
    ;;
  startup)
    run_startup
    ;;
  *)
    echo "Unknown command: ${command_name}" >&2
    usage >&2
    exit 1
    ;;
esac
