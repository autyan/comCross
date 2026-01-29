#!/usr/bin/env bash
set -euo pipefail

config="Release"
output_dir="artifacts"
rids=(linux-x64 linux-arm64 win-x64 win-arm64)
publish=false

get_plugin_id_from_manifest() {
  local manifest_path="$1"

  if command -v python3 >/dev/null 2>&1; then
    python3 - <<'PY' "$manifest_path"
import json, sys
with open(sys.argv[1], 'r', encoding='utf-8') as f:
    data = json.load(f)
plugin_id = (data.get('id') or '').strip()
if not plugin_id:
    raise SystemExit('manifest missing id')
print(plugin_id)
PY
    return
  fi

  # Fallback: minimal JSON extraction without external deps.
  local extracted
  extracted="$(grep -m1 '"id"' "${manifest_path}" | sed -E 's/.*"id"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/')"
  if [[ -z "${extracted}" ]]; then
    echo "manifest missing id" >&2
    return 1
  fi
  printf '%s\n' "${extracted}"
}

stable_hash_for_plugin_id() {
  local plugin_id="$1"

  if command -v python3 >/dev/null 2>&1; then
    python3 - <<'PY' "$plugin_id"
import base64, hashlib, sys
pid = sys.argv[1]
h = hashlib.sha256(pid.encode('utf-8')).digest()
print(base64.b32encode(h).decode('ascii')[:8].lower())
PY
    return
  fi

  if command -v openssl >/dev/null 2>&1 && command -v base32 >/dev/null 2>&1; then
    printf '%s' "${plugin_id}" \
      | openssl dgst -sha256 -binary \
      | base32 \
      | tr -d '=' \
      | tr 'A-Z' 'a-z' \
      | head -c 8
    printf '\n'
    return
  fi

  echo "Unable to compute stableHash for pluginId (need python3 or openssl+base32)." >&2
  return 1
}

publish_plugins() {
  local out_path="$1"
  local rid="$2"
  local plugins_dir="${out_path}/plugins"

  rm -rf "${plugins_dir}"
  mkdir -p "${plugins_dir}"

  for plugin_proj in src/Plugins/*/*.csproj; do
    local plugin_dir
    plugin_dir="$(dirname "${plugin_proj}")"

    local manifest_path
    manifest_path="${plugin_dir}/Resources/ComCross.Plugin.Manifest.json"
    if [[ ! -f "${manifest_path}" ]]; then
      echo "[publish] Missing manifest: ${manifest_path}" >&2
      exit 1
    fi

    local plugin_id
    plugin_id="$(get_plugin_id_from_manifest "${manifest_path}")"

    local stable_hash
    stable_hash="$(stable_hash_for_plugin_id "${plugin_id}")"

    local plugin_out_dir
    plugin_out_dir="${plugins_dir}/${plugin_id}-${stable_hash}"

    dotnet publish "${plugin_proj}" \
      -c "${config}" \
      -r "${rid}" \
      --self-contained false \
      -o "${plugin_out_dir}"
  done
}

usage() {
  cat <<'EOF'
Usage: scripts/build.sh [-c CONFIG] [-o OUTPUT_DIR] [-r RID_LIST] [--publish]

Options:
  -c, --config      Build configuration (Debug|Release). Default: Release
  -o, --output      Output directory. Default: artifacts
  -r, --rids        Comma-separated RIDs (e.g. linux-x64,linux-x86,win-x86)
  --publish         Publish instead of build
  -h, --help        Show help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--config)
      config="${2:-}"
      shift 2
      ;;
    -o|--output)
      output_dir="${2:-}"
      shift 2
      ;;
    -r|--rids)
      IFS=',' read -r -a rids <<< "${2:-}"
      shift 2
      ;;
    --publish)
      publish=true
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

# Repository guardrails (architectural boundaries)
if [[ "${COMCROSS_SKIP_GUARDRAILS:-}" != "1" ]]; then
  bash "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/repo-tools/check-project-boundaries.sh"

  root_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

  # i18n guardrails: strict locally; warn-only in CI/CD (or when explicitly requested)
  # - COMCROSS_STRICT_I18N=1 forces strict mode even in CI
  # - COMCROSS_I18N_WARN_ONLY=1 forces warn-only mode even locally
  warn_only=false
  if [[ "${COMCROSS_I18N_WARN_ONLY:-}" == "1" ]]; then
    warn_only=true
  elif [[ -n "${CI:-}" && "${COMCROSS_STRICT_I18N:-}" != "1" ]]; then
    warn_only=true
  fi

  if [[ "${warn_only}" == "true" ]]; then
    set +e
    bash "${root_dir}/repo-tools/check-shell-i18n.sh"
    status=$?
    if [[ ${status} -ne 0 ]]; then
      echo "[warn] Shell i18n scan reported issues (exit ${status}); not failing CI." >&2
    fi

    bash "${root_dir}/repo-tools/check-shell-i18n-keys.sh"
    status=$?
    if [[ ${status} -ne 0 ]]; then
      echo "[warn] Shell i18n key check reported issues (exit ${status}); not failing CI." >&2
    fi
    set -e
  else
    bash "${root_dir}/repo-tools/check-shell-i18n.sh"
    bash "${root_dir}/repo-tools/check-shell-i18n-keys.sh"
  fi
fi

if [[ -z "${config}" ]]; then
  echo "Config cannot be empty." >&2
  exit 1
fi

if [[ "${publish}" == "true" ]]; then
  if [[ ${#rids[@]} -eq 0 ]]; then
    echo "RID list cannot be empty for publish." >&2
    exit 1
  fi

  for rid in "${rids[@]}"; do
    out_path="${output_dir}/ComCross-${rid}-${config}"
    dotnet publish src/Shell/ComCross.Shell.csproj -c "${config}" -r "${rid}" --self-contained false -o "${out_path}"
    dotnet publish src/PluginHost/ComCross.PluginHost.csproj -c "${config}" -r "${rid}" --self-contained false -o "${out_path}"
    dotnet publish src/SessionHost/ComCross.SessionHost.csproj -c "${config}" -r "${rid}" --self-contained false -o "${out_path}"
    publish_plugins "${out_path}" "${rid}"
  done
else
  dotnet build src/Shell/ComCross.Shell.csproj -c "${config}"
fi
