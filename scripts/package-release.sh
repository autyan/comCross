#!/usr/bin/env bash
set -euo pipefail

config="Release"
output_dir="artifacts"
rids=(linux-x64 linux-arm64 win-x64 win-arm64)
include_symbols=false
package_outputs=true
target_framework="net10.0"

usage() {
  cat <<'EOF'
Usage: scripts/package-release.sh [-c CONFIG] [-o OUTPUT_DIR] [-r RID_LIST] [--include-symbols]

Builds both framework-dependent and self-contained release outputs for all RIDs.

Options:
  -c, --config           Build configuration. Default: Release
  -o, --output           Output directory. Default: artifacts
  -r, --rids             Comma-separated RIDs (e.g. linux-x64,linux-arm64,win-x64)
  --include-symbols      Keep PDB symbols in outputs (default: false)
  --no-package           Skip creating .zip/.tar.gz archives
  -h, --help             Show help
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
    --include-symbols)
      include_symbols=true
      shift
      ;;
    --no-package)
      package_outputs=false
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

symbol_args=()
if [[ "${include_symbols}" == "false" ]]; then
  symbol_args+=("-p:DebugType=none" "-p:DebugSymbols=false")
fi

publish_project() {
  local project="$1"
  local out_path="$2"
  local rid="$3"
  local self_contained="$4"

  dotnet publish "${project}" \
    -c "${config}" \
    -r "${rid}" \
    --self-contained "${self_contained}" \
    -o "${out_path}" \
    "${symbol_args[@]}"
}

build_plugins() {
  local config_name="$1"
  dotnet build src/Plugins/ComCross.Plugins.Stats/ComCross.Plugins.Stats.csproj -c "${config_name}"
  dotnet build src/Plugins/ComCross.Plugins.Protocol/ComCross.Plugins.Protocol.csproj -c "${config_name}"
  dotnet build src/Plugins/ComCross.Plugins.Flow/ComCross.Plugins.Flow.csproj -c "${config_name}"
}

copy_plugins() {
  local out_path="$1"
  local plugins_dir="${out_path}/plugins"
  
  mkdir -p "${plugins_dir}"
  
  # Copy official plugins from src/Plugins/*/bin/<config>/<tfm>/
  for plugin_dir in src/Plugins/*/; do
    if [[ -d "${plugin_dir}bin/${config}/${target_framework}/" ]]; then
      # Copy plugin DLL (not ComCross.Shared.dll as it's already in main directory)
      find "${plugin_dir}bin/${config}/${target_framework}/" -maxdepth 1 -name "ComCross.Plugins.*.dll" -exec cp {} "${plugins_dir}/" \;
    fi
  done
  
  echo "Copied plugins to ${plugins_dir}"
}

create_archive() {
  local source_dir="$1"
  local archive_path="$2"
  python - "$source_dir" "$archive_path" <<'PY'
import os
import sys
import tarfile
import zipfile

source = sys.argv[1]
target = sys.argv[2]

os.makedirs(os.path.dirname(target), exist_ok=True)

if target.endswith(".zip"):
    with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED) as zf:
        for root, _, files in os.walk(source):
            for name in files:
                full = os.path.join(root, name)
                rel = os.path.relpath(full, source)
                zf.write(full, rel)
else:
    with tarfile.open(target, "w:gz") as tf:
        tf.add(source, arcname=".")
PY
}

build_plugins "${config}"

for rid in "${rids[@]}"; do
  fd_out="${output_dir}/framework-dependent/ComCross-${rid}-${config}"
  sc_out="${output_dir}/self-contained/ComCross-${rid}-${config}"

  publish_project src/Shell/ComCross.Shell.csproj "${fd_out}" "${rid}" false
  publish_project src/PluginHost/ComCross.PluginHost.csproj "${fd_out}" "${rid}" false
  copy_plugins "${fd_out}"

  publish_project src/Shell/ComCross.Shell.csproj "${sc_out}" "${rid}" true
  publish_project src/PluginHost/ComCross.PluginHost.csproj "${sc_out}" "${rid}" true
  copy_plugins "${sc_out}"

  if [[ "${package_outputs}" == "true" ]]; then
    if [[ "${rid}" == win-* ]]; then
      create_archive "${fd_out}" "${output_dir}/packages/framework-dependent/ComCross-${rid}-${config}.zip"
      create_archive "${sc_out}" "${output_dir}/packages/self-contained/ComCross-${rid}-${config}.zip"
    else
      create_archive "${fd_out}" "${output_dir}/packages/framework-dependent/ComCross-${rid}-${config}.tar.gz"
      create_archive "${sc_out}" "${output_dir}/packages/self-contained/ComCross-${rid}-${config}.tar.gz"
    fi
  fi
done
