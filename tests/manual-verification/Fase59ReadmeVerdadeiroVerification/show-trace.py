import json
import sys
import urllib.parse
import urllib.request

BASE_URL = "http://127.0.0.1:5059/flow/api"


def get_json(path):
    with urllib.request.urlopen(BASE_URL + path, timeout=20) as response:
        return json.load(response)


def latest_trace_id(route):
    query = urllib.parse.urlencode({"route": route, "pageSize": 1})
    items = get_json(f"/traces?{query}")["items"]
    return items[0]["traceId"] if items else None


def print_trace(route):
    trace_id = latest_trace_id(route)
    if trace_id is None:
        print(f"[{route}] no trace recorded")
        return
    detail = get_json(f"/traces/{trace_id}")
    print(f"[{route}] status={detail['status']} http={detail['httpStatus']} spans={detail['spanCount']} tags={detail['tags']}")
    for span in sorted(detail["spans"], key=lambda item: item["sequence"]):
        error = f" error={span['errorType']}: {span['errorMessage']!r}" if span.get("errorMessage") else ""
        print(
            f"    {span['sequence']:>2} {span['type']:<12} {span['component']}.{span['operation']} "
            f"{span['durationMicros'] / 1000:.2f}ms {span['status']}{error}"
        )


def main():
    for route in sys.argv[1:]:
        print_trace(route)


if __name__ == "__main__":
    main()
