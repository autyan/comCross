#!/usr/bin/env bash
set -euo pipefail

if ! command -v gdbus >/dev/null 2>&1; then
  echo "gdbus is required" >&2
  exit 1
fi

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required" >&2
  exit 1
fi

timeout_secs="${1:-15}"
monitor_log="$(mktemp)"

cleanup() {
  if [[ -n "${monitor_pid:-}" ]]; then
    kill "${monitor_pid}" >/dev/null 2>&1 || true
    wait "${monitor_pid}" 2>/dev/null || true
  fi
  rm -f "${monitor_log}"
}

trap cleanup EXIT

gdbus monitor \
  --session \
  --dest org.freedesktop.portal.Desktop \
  >"${monitor_log}" 2>&1 &
monitor_pid=$!

sleep 0.5

request_output="$(gdbus call \
  --session \
  --dest org.freedesktop.portal.Desktop \
  --object-path /org/freedesktop/portal/desktop \
  --method org.freedesktop.portal.Screenshot.Screenshot \
  '' \
  "{'interactive': <false>, 'modal': <false>}")"

request_path="$(python3 - <<'PY' "${request_output}"
import re
import sys

text = sys.argv[1]
match = re.search(r"'([^']+)'", text)
if not match:
    raise SystemExit(1)
print(match.group(1))
PY
)"

deadline=$((SECONDS + timeout_secs))
uri=""
while (( SECONDS < deadline )); do
  if grep -Fq "${request_path}" "${monitor_log}" && grep -Fq "uri" "${monitor_log}"; then
    uri="$(python3 - <<'PY' "${monitor_log}" "${request_path}"
import pathlib
import re
import sys
import urllib.parse

log_path = pathlib.Path(sys.argv[1])
request_path = sys.argv[2]
text = log_path.read_text(encoding="utf-8", errors="ignore")
chunks = text.split(request_path)
for chunk in reversed(chunks[1:]):
    match = re.search(r"['\"]uri['\"].*?['\"](file:[^'\"]+)['\"]", chunk, re.S)
    if match:
        print(urllib.parse.unquote(match.group(1)))
        break
PY
)"
    if [[ -n "${uri}" ]]; then
      break
    fi
  fi
  sleep 0.25
done

if [[ -z "${uri}" ]]; then
  echo "Timed out waiting for portal screenshot response" >&2
  exit 1
fi

python3 - <<'PY' "${uri}"
import sys
import urllib.parse

uri = sys.argv[1]
if not uri.startswith("file:"):
    raise SystemExit(1)
print(urllib.parse.urlparse(uri).path)
PY
