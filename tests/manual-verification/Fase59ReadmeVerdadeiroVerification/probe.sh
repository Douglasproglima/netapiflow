#!/usr/bin/env bash
set -u

here="$(cygpath -m "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)")"
dll="${PROBE_DLL:-$here/ProbeApp/bin/Release/${PROBE_TFM:-net10.0}/ProbeApp.dll}"
port="${PROBE_PORT:-5059}"

stop_listener() {
  local listening_port="$1"
  for pid in $(netstat -ano | grep ":${listening_port} .*LISTENING" | awk '{print $5}' | sort -u); do
    MSYS_NO_PATHCONV=1 taskkill /F /T /PID "$pid" > /dev/null 2>&1
  done
}

start_probe() {
  local log_file="$1"
  shift
  stop_listener "$port"
  (env ASPNETCORE_ENVIRONMENT="${PROBE_ENVIRONMENT:-Development}" "$@" dotnet "$dll" > "$log_file" 2>&1 &)
  for _ in $(seq 1 40); do
    if grep -q "Application started" "$log_file" 2>/dev/null; then
      return 0
    fi
    sleep 1
  done
  echo "probe did not start; see $log_file" >&2
  return 1
}

start_receiver() {
  stop_listener 4318
  (python -I "$here/otlp-receiver.py" 4318 > /dev/null 2>&1 &)
  for _ in $(seq 1 20); do
    if curl -s -X POST http://127.0.0.1:4318/reset > /dev/null; then
      return 0
    fi
    sleep 1
  done
  return 1
}

reset_receiver() {
  curl -s -X POST http://127.0.0.1:4318/reset > /dev/null
}

dump_receiver() {
  curl -s http://127.0.0.1:4318/dump
}

assert_single_listener() {
  local listening_port="$1"
  local listeners
  listeners="$(netstat -ano | grep ":${listening_port} .*LISTENING" | awk '{print $5}' | sort -u | wc -l)"
  if [ "$listeners" -ne 1 ]; then
    echo "expected exactly one listener on port ${listening_port}, found ${listeners}" >&2
    return 1
  fi
}

command_name="${1:-}"
shift || true

case "$command_name" in
  start-probe) start_probe "$@" && assert_single_listener "$port" ;;
  stop-probe) stop_listener "$port" ;;
  start-receiver) start_receiver && assert_single_listener 4318 ;;
  stop-receiver) stop_listener 4318 ;;
  reset-receiver) reset_receiver ;;
  dump-receiver) dump_receiver ;;
  *) echo "usage: probe.sh start-probe <log> [VAR=value ...] | stop-probe | start-receiver | stop-receiver | reset-receiver | dump-receiver" >&2; exit 2 ;;
esac
