#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${COMCROSS_SKIP_GUARDRAILS:-}" == "1" ]]; then
  echo "[guardrails] Skipped (COMCROSS_SKIP_GUARDRAILS=1)."
  exit 0
fi

fail=0

check_no_project_references() {
  local label="$1"
  local dir_rel="$2"
  local dir_abs="${root_dir}/${dir_rel}"

  if [[ ! -d "${dir_abs}" ]]; then
    echo "[guardrails] ${label}: directory not found: ${dir_rel}" >&2
    fail=1
    return
  fi

  local matches
  matches="$(grep -R --line-number --fixed-string "<ProjectReference" \
    --exclude-dir bin --exclude-dir obj \
    "${dir_abs}" 2>/dev/null || true)"

  if [[ -n "${matches}" ]]; then
    echo "[guardrails] FAIL: ${label} must not reference in-repo projects (no <ProjectReference>)." >&2
    echo "[guardrails] Found ProjectReference entries under ${dir_rel}:" >&2
    echo "${matches}" >&2
    fail=1
  else
    echo "[guardrails] OK: ${label} has no ProjectReference." 
  fi
}

check_no_project_references "Platform" "src/Platform"
check_no_project_references "PluginSdk" "src/PluginSdk"

if [[ "${fail}" -ne 0 ]]; then
  echo "[guardrails] One or more checks failed." >&2
  exit 1
fi

echo "[guardrails] All checks passed."
