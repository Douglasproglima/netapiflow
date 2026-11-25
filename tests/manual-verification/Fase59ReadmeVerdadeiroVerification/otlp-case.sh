#!/usr/bin/env bash
set -u

here="$(cygpath -m "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)")"
probe="$here/probe.sh"
log_directory="${PROBE_LOG_DIRECTORY:-${TMPDIR:-/tmp}}"
export MSYS_NO_PATHCONV=1

case_name="$1"
request_paths="$2"
shift 2

"$probe" stop-probe
"$probe" stop-receiver
"$probe" start-receiver || exit 1
"$probe" start-probe "$log_directory/$case_name.log" Probe__Otlp=true "$@" || exit 1
"$probe" reset-receiver

echo "=== $case_name  (env: $*)"
IFS=',' read -ra paths <<< "$request_paths"
for request_path in "${paths[@]}"; do
  status="$(curl -s -o NUL -w '%{http_code}' "http://127.0.0.1:5059${request_path}")"
  echo "GET ${request_path} -> ${status}"
done
sleep 3

echo "--- NetApiFlow dashboard recorded:"
curl -s "http://127.0.0.1:5059/flow/api/traces?pageSize=50" | grep -o '"routeTemplate":"[^"]*"' | sort | uniq -c
echo "--- OTLP receiver got:"
"$probe" dump-receiver | python -I "$here/summarize-spans.py"

"$probe" stop-probe
"$probe" stop-receiver
