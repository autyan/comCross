#!/usr/bin/env bash
set -euo pipefail

config="Release"
output_dir="artifacts"
rids=(linux-x64 linux-arm64 win-x64 win-arm64)
publish=false

publish_plugins() {
  local out_path="$1"
  local rid="$2"
  local plugins_dir="${out_path}/plugins"

  rm -rf "${plugins_dir}"
  mkdir -p "${plugins_dir}"

  for plugin_proj in src/Plugins/*/*.csproj; do
    local plugin_id
    plugin_id="$(python - <<'PY'
import uuid
print(uuid.uuid4().hex)
PY
)"

    dotnet publish "${plugin_proj}" \
      -c "${config}" \
      -r "${rid}" \
      --self-contained false \
      -o "${plugins_dir}/${plugin_id}"
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
    publish_plugins "${out_path}" "${rid}"
  done
else
  dotnet build src/Shell/ComCross.Shell.csproj -c "${config}"
fi
