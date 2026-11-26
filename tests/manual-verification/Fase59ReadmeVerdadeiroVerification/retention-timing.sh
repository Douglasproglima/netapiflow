#!/usr/bin/env bash
set -u

here="$(cygpath -m "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)")"
export MSYS_NO_PATHCONV=1
export PROBE_PORT=5060
log_file="${1:?usage: retention-timing.sh <log-file>}"
max_age_seconds=15
sample_every_seconds=20
total_seconds=330

"$here/probe.sh" start-probe "$log_file.app" Probe__Port=5060 Probe__Retention__MaxAgeSeconds="$max_age_seconds" || exit 1

for _ in 1 2 3 4 5; do
  curl -s -o NUL "http://127.0.0.1:5060/hello"
done

started_at=$(date +%s)
echo "MaxAge=${max_age_seconds}s, 5 traces created at t=0, default retention interval is 5 minutes" > "$log_file"
while true; do
  elapsed=$(( $(date +%s) - started_at ))
  count=$(curl -s "http://127.0.0.1:5060/flow/api/traces?pageSize=1" | python -I -c "import sys,json; print(json.load(sys.stdin)['totalCount'])")
  echo "t=${elapsed}s traces=${count}" >> "$log_file"
  if [ "$elapsed" -ge "$total_seconds" ]; then
    break
  fi
  sleep "$sample_every_seconds"
done

"$here/probe.sh" stop-probe
echo DONE >> "$log_file"
