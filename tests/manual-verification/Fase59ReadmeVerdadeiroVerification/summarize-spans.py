import json
import sys

KIND_NAMES = {0: "UNSPECIFIED", 1: "INTERNAL", 2: "SERVER", 3: "CLIENT", 4: "PRODUCER", 5: "CONSUMER"}
URL_KEYS = ("url.full", "http.url", "url.query", "url.path", "http.target", "server.address")


def describe(span):
    parent = span["parentSpanId"] or "none"
    url_attributes = {key: span["attributes"][key] for key in URL_KEYS if key in span["attributes"]}
    return (
        f'{KIND_NAMES.get(span["kind"], span["kind"]):<11} '
        f'scope={span["scope"]:<28} name={span["name"]!r} '
        f'id={span["spanId"][:6]} parent={parent[:6]} {url_attributes if url_attributes else ""}'
    )


def main():
    spans = json.load(sys.stdin)
    service_names = sorted({str(span.get("resource", {}).get("service.name")) for span in spans})
    print(f"{len(spans)} span(s) received, service.name={service_names}")
    for span in spans:
        print("  " + describe(span))
    roots = [span for span in spans if not span["parentSpanId"]]
    print(f"{len(roots)} root span(s) with no parent: {[root['name'] for root in roots]}")
    server_spans = [span for span in spans if span["kind"] == 2]
    print(f"{len(server_spans)} SERVER span(s)")


if __name__ == "__main__":
    main()
