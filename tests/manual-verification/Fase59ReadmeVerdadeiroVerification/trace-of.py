import json
import sys
import urllib.request

BASE_URL = "http://127.0.0.1:5059"


def main():
    path = sys.argv[1]
    with urllib.request.urlopen(BASE_URL + path, timeout=20) as response:
        trace_id = response.headers.get("X-NetApiFlow-TraceId") or response.headers.get("X-Probe-TraceId")
        status = response.status
    print(f"GET {path} -> {status} trace={trace_id}")
    if trace_id is None:
        return
    with urllib.request.urlopen(f"{BASE_URL}/trace-of?id={trace_id}", timeout=20) as response:
        detail = json.load(response)
    spans = detail.get("spans") or detail.get("Spans") or []
    for span in sorted(spans, key=lambda item: item.get("sequence", item.get("Sequence", 0))):
        get = lambda name: span.get(name[0].lower() + name[1:], span.get(name))
        print(f"    {get('Sequence'):>2} {get('Type')} {get('Component')}.{get('Operation')} {get('Status')}")


if __name__ == "__main__":
    main()
