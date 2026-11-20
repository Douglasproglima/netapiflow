set -u
TFM=$1
MODE=$2
VERSION=$3
PORT=5150
HERE=$(cd "$(dirname "$0")" && pwd)
cd "$HERE"

export SMOKE_PG='Host=localhost;Port=54320;Username=postgres;Password=pw;Database=postgres'
export ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:$PORT
[ "$MODE" = "control" ] && export SMOKE_CONTROL=1 || unset SMOKE_CONTROL
export SmokeTfm=$TFM SmokePackageVersion=$VERSION

stop_app() {
  powershell -NoProfile -Command "\$c = Get-NetTCPConnection -LocalPort $PORT -State Listen -ErrorAction SilentlyContinue; if (\$c) { Stop-Process -Id \$c.OwningProcess -Force }" > /dev/null
}

start_app() {
  (dotnet run --no-build > "app-$TFM-$MODE.log" 2>&1 &)
  for _ in $(seq 1 30); do
    curl -s -o /dev/null "localhost:$PORT/flow/" && return 0
    sleep 1
  done
  echo "app did not start"; return 1
}

rm -rf obj bin
dotnet restore -nologo -v q 2>&1 | grep -E "error"
echo "--- assets ($TFM)"
python - <<'PY'
import json
a = json.load(open("obj/project.assets.json"))
for target in a["targets"].values():
    for name, info in target.items():
        if name.startswith("NetApiFlow"):
            print(name, sorted(info.get("runtime", {}).keys())[:1])
PY
dotnet build --no-restore -nologo -v q 2>&1 | grep -E "Erro\(s\)|error"

trace_count() {
  curl -s "localhost:$PORT/flow/api/traces?limit=1" | python -c "import json,sys;print(json.load(sys.stdin)['totalCount'])" 2>/dev/null || echo 0
}

wait_for_persisted_trace() {
  for waited in $(seq 1 30); do
    [ "$(trace_count)" -gt "$1" ] && { echo "  trace persisted after ${waited}s"; return 0; }
    sleep 1
  done
  echo "  trace NOT persisted after 30s"
}

show_trace() {
  before=$(trace_count)
  local path=$1
  curl -s -o /dev/null -w "$path request:%{http_code}\n" "localhost:$PORT$path"
  wait_for_persisted_trace "$before"
  TRACE=$(curl -s "localhost:$PORT/flow/api/traces?limit=1" | python -c "import json,sys;print(json.load(sys.stdin)['items'][0]['traceId'])")
  curl -s "localhost:$PORT/flow/api/traces/$TRACE" | python -c "
import json,sys
d=json.load(sys.stdin)
for s in d['spans']:
    print('  ', s['component'], s['type'], 'spanId=' + s['spanId'], 'parentSpanId=' + str(s['parentSpanId']))
print('  npgsql spans:', sum(1 for s in d['spans'] if s['component'] == 'Npgsql'),
      '| database spans:', sum(1 for s in d['spans'] if s['type'] == 'Database'))"
}

stop_app
start_app || exit 1
for route in /api/raw /api/step /mini/step /api/ef; do
  show_trace $route
done

stop_app
start_app || exit 1
curl -s "localhost:$PORT/flow/api/traces?limit=50" | python -c "
import json,sys
print('trace still there after restart:', '$TRACE' in [i['traceId'] for i in json.load(sys.stdin)['items']])"
curl -s -o /dev/null -w "dashboard:%{http_code} %{content_type}\n" "localhost:$PORT/flow/"
stop_app
dotnet list package --vulnerable --include-transitive 2>&1
