#!/usr/bin/env bash
set -euo pipefail

config="Release"
output_dir="artifacts"
rids=(linux-x64 linux-arm64 win-x64 win-arm64)
include_symbols=false
package_outputs=true
channel="Stable"
directory_name=""
instance_id=""
schema_line="v0"
plugin_signing_key=""
plugin_signing_key_id="comcross-plugin-official-2026"
require_plugin_signing=false

usage() {
  cat <<'EOF'
Usage: scripts/package-release.sh [-c CONFIG] [-o OUTPUT_DIR] [-r RID_LIST] [--include-symbols]

Builds both framework-dependent and self-contained release outputs for all RIDs.

Options:
  -c, --config           Build configuration. Default: Release
  -o, --output           Output directory. Default: artifacts
  -r, --rids             Comma-separated RIDs (e.g. linux-x64,linux-arm64,win-x64)
  --channel CHANNEL      Instance channel: Stable, Dev, or EAP. Default: Stable
  --directory-name NAME  Override instance directoryName
  --instance-id ID       Override instance id
  --schema-line VALUE    Instance schema line. Default: v0
  --plugin-signing-key FILE
                          PEM private key used to sign bundled official plugins
  --plugin-signing-key-id ID
                          Plugin signing key id. Default: comcross-plugin-official-2026
  --require-plugin-signing
                          Fail if plugin signing key is missing
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

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=scripts/release/common.sh
source "${script_dir}/release/common.sh"

read -r default_directory_name default_instance_id < <(release_channel_defaults "${channel}")
if [[ -z "${directory_name}" ]]; then
  directory_name="${default_directory_name}"
fi
if [[ -z "${instance_id}" ]]; then
  instance_id="${default_instance_id}"
fi
if [[ -z "${schema_line}" ]]; then
  echo "Schema line cannot be empty." >&2
  exit 1
fi
if [[ "${require_plugin_signing}" == "true" && -z "${plugin_signing_key}" ]]; then
  echo "--require-plugin-signing requires --plugin-signing-key." >&2
  exit 1
fi
if [[ -n "${plugin_signing_key}" && ! -f "${plugin_signing_key}" ]]; then
  echo "Plugin signing key not found: ${plugin_signing_key}" >&2
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

write_instance_manifest() {
  local out_path="$1"
  python - "$out_path" "$channel" "$directory_name" "$instance_id" "$schema_line" <<'PY'
import json
import os
import sys

out_path, channel, directory_name, instance_id, schema_line = sys.argv[1:]
manifest = {
    "schemaVersion": 1,
    "product": "ComCross",
    "channel": channel,
    "directoryName": directory_name,
    "instanceId": instance_id,
    "schemaLine": schema_line,
}
with open(os.path.join(out_path, "ComCross.Instance.json"), "w", encoding="utf-8") as f:
    json.dump(manifest, f, indent=2)
    f.write("\n")
PY
}

sign_plugin_package() {
  local plugin_out="$1"

  if [[ -z "${plugin_signing_key}" ]]; then
    return 0
  fi

  dotnet run --project src/Tools/ComCross.Tools.csproj -- \
    sign-plugin \
    --plugin-dir "${plugin_out}" \
    --private-key "${plugin_signing_key}" \
    --key-id "${plugin_signing_key_id}"
}

build_plugins() {
  local config_name="$1"
  for plugin_proj in src/Plugins/*/*.csproj; do
    dotnet build "${plugin_proj}" -c "${config_name}"
  done
}

copy_plugins() {
  local out_path="$1"
  local rid="$2"
  local plugins_dir="${out_path}/bundled-plugins"
  local legacy_plugins_dir="${out_path}/plugins"
  
  rm -rf "${legacy_plugins_dir}"
  rm -rf "${plugins_dir}"
  mkdir -p "${plugins_dir}"

  # Track folder-name collisions within one packaging run.
  declare -A seen_plugin_folders=()
  
  # Publish official plugins into isolated folders so each plugin carries its own deps + native assets.
  # This fixes runtime load failures like missing System.IO.Ports / IO.Serial in the plugin process.
  for plugin_proj in src/Plugins/*/*.csproj; do
    local manifest_path
    manifest_path="$(dirname "${plugin_proj}")/Resources/ComCross.Plugin.Manifest.json"
    if [[ ! -f "${manifest_path}" ]]; then
      echo "Missing plugin manifest: ${manifest_path}" >&2
      exit 1
    fi

    # Deterministic folder naming: <pluginId>-<stableHash>
    # stableHash = base32(sha256(pluginId))[0:8].lower()
    local plugin_folder
    plugin_folder="$(python - "${manifest_path}" <<'PY'
import base64
import hashlib
import json
import re
import sys

manifest_path = sys.argv[1]
with open(manifest_path, "r", encoding="utf-8") as f:
    data = json.load(f)

plugin_id = data.get("id")
if not isinstance(plugin_id, str) or not plugin_id.strip():
    raise SystemExit(f"Manifest missing non-empty 'id': {manifest_path}")

# Keep folder name filesystem-safe and predictable.
if not re.fullmatch(r"[A-Za-z0-9._-]+", plugin_id):
    raise SystemExit(f"Invalid plugin id '{plugin_id}' in {manifest_path}. Allowed: [A-Za-z0-9._-]+")

h = hashlib.sha256(plugin_id.encode("utf-8")).digest()
suffix = base64.b32encode(h).decode("ascii").rstrip("=").lower()[:8]
print(f"{plugin_id}-{suffix}")
PY
)"
    local plugin_out
    plugin_out="${plugins_dir}/${plugin_folder}"

    if [[ -n "${seen_plugin_folders["${plugin_folder}"]+x}" ]]; then
      echo "Duplicate plugin folder name computed: ${plugin_folder}" >&2
      exit 1
    fi
    seen_plugin_folders["${plugin_folder}"]=1

    rm -rf "${plugin_out}"
    mkdir -p "${plugin_out}"

    dotnet publish "${plugin_proj}" \
      -c "${config}" \
      -r "${rid}" \
      --self-contained false \
      -o "${plugin_out}" \
      "${symbol_args[@]}"

    sign_plugin_package "${plugin_out}"
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
import io

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
        # Portable release marker (empty file). This is intentionally added only to tar.*
        # archives so it won't leak into DEB/RPM packaging that reuses publish outputs.
        info = tarfile.TarInfo(name="comcross.portable")
        info.size = 0
        tf.addfile(info, fileobj=io.BytesIO(b""))
PY
}

build_plugins "${config}"

for rid in "${rids[@]}"; do
  fd_out="${output_dir}/framework-dependent/ComCross-${rid}-${config}"
  sc_out="${output_dir}/self-contained/ComCross-${rid}-${config}"

  publish_project src/Shell/ComCross.Shell.csproj "${fd_out}" "${rid}" false
  publish_project src/Startup/ComCross.Startup.csproj "${fd_out}" "${rid}" false
  publish_project src/PluginHost/ComCross.PluginHost.csproj "${fd_out}" "${rid}" false
  publish_project src/ExtensionHost/ComCross.ExtensionHost.csproj "${fd_out}" "${rid}" false
  publish_project src/SessionHost/ComCross.SessionHost.csproj "${fd_out}" "${rid}" false
  write_instance_manifest "${fd_out}"
  copy_plugins "${fd_out}" "${rid}"

  publish_project src/Shell/ComCross.Shell.csproj "${sc_out}" "${rid}" true
  publish_project src/Startup/ComCross.Startup.csproj "${sc_out}" "${rid}" true
  publish_project src/PluginHost/ComCross.PluginHost.csproj "${sc_out}" "${rid}" true
  publish_project src/ExtensionHost/ComCross.ExtensionHost.csproj "${sc_out}" "${rid}" true
  publish_project src/SessionHost/ComCross.SessionHost.csproj "${sc_out}" "${rid}" true
  write_instance_manifest "${sc_out}"
  copy_plugins "${sc_out}" "${rid}"

  if [[ "${package_outputs}" == "true" ]]; then
    if [[ "${rid}" == win-* ]]; then
      create_archive "${fd_out}" "${output_dir}/packages/framework-dependent/ComCross-${rid}-${config}-framework-dependent.zip"
      create_archive "${sc_out}" "${output_dir}/packages/self-contained/ComCross-${rid}-${config}-self-contained.zip"
    else
      create_archive "${fd_out}" "${output_dir}/packages/framework-dependent/ComCross-${rid}-${config}-framework-dependent.tar.gz"
      create_archive "${sc_out}" "${output_dir}/packages/self-contained/ComCross-${rid}-${config}-self-contained.tar.gz"
    fi
  fi
done
