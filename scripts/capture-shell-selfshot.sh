#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: $0 <output-path> [delay-ms] [startup-surface]" >&2
  exit 1
fi

output_path="$1"
delay_ms="${2:-2200}"
startup_surface="${3:-}"

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
shell_bin="${repo_root}/src/Shell/bin/Debug/net8.0/ComCross.Shell"

if [[ ! -x "${shell_bin}" ]]; then
  echo "Shell binary not found or not executable: ${shell_bin}" >&2
  echo "Run: dotnet build src/Shell/ComCross.Shell.csproj" >&2
  exit 1
fi

mkdir -p "$(dirname "${output_path}")"
rm -f "${output_path}"

COMCROSS_SHELL_AUTO_SCREENSHOT="${output_path}" \
COMCROSS_SHELL_AUTO_SCREENSHOT_DELAY_MS="${delay_ms}" \
COMCROSS_SHELL_AUTO_EXIT_AFTER_SCREENSHOT=1 \
COMCROSS_SHELL_START_SURFACE="${startup_surface}" \
  "${shell_bin}"

if [[ ! -f "${output_path}" ]]; then
  echo "Screenshot was not produced: ${output_path}" >&2
  exit 1
fi

echo "${output_path}"
