set -u
TFM=$1
MODE=$2
VERSION=$3
PORT=5150
HERE=$(cd "$(dirname "$0")" && pwd)
cd "$HERE"

export SMOKE_SQL='Server=localhost,14330;Database=smoke;User Id=sa;Password=Smoke050!Pass;TrustServerCertificate=True;Encrypt=True'
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

stop_app
start_app || exit 1
show_trace() {
  local label=$1 path=$2
  curl -s -o /dev/null -w "$label request:%{http_code}
" "localhost:$PORT$path"
  sleep 3
  TRACE=$(curl -s "localhost:$PORT/flow/api/traces?limit=1" | python -c "import json,sys;print(json.load(sys.stdin)['items'][0]['traceId'])")
  curl -s "localhost:$PORT/flow/api/traces/$TRACE" | python -c "
import json,sys
d=json.load(sys.stdin)
print('$label spans:', sorted({(s['component'], s['type']) for s in d['spans']}))
print('$label logs on spans:', [l['message'] for s in d['spans'] for l in s['logs']])"
}

show_trace controller /api/smoke
show_trace minimal-api /mini/plain

stop_app
start_app || exit 1
curl -s "localhost:$PORT/flow/api/traces?limit=50" | python -c "
import json,sys
print('trace still there after restart:', '$TRACE' in [i['traceId'] for i in json.load(sys.stdin)['items']])"
curl -s -o /dev/null -w "dashboard:%{http_code} %{content_type}\n" "localhost:$PORT/flow/"
stop_app
dotnet list package --vulnerable --include-transitive 2>&1 | grep -iE "vulner"
