#!/usr/bin/env bash
set -euo pipefail

root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if [[ "${COMCROSS_SKIP_GUARDRAILS:-}" == "1" ]]; then
  echo "[i18n] Skipped (COMCROSS_SKIP_GUARDRAILS=1)."
  exit 0
fi

python3 "${root_dir}/repo-tools/check-shell-i18n.py" "${root_dir}"
