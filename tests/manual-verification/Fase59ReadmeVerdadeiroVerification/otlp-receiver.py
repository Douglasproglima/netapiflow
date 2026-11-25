import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

WIRE_VARINT = 0
WIRE_FIXED64 = 1
WIRE_LENGTH_DELIMITED = 2
WIRE_FIXED32 = 5

received_spans = []
received_lock = threading.Lock()


def read_varint(buffer, position):
    shift = 0
    value = 0
    while True:
        byte = buffer[position]
        position += 1
        value |= (byte & 0x7F) << shift
        if not byte & 0x80:
            return value, position
        shift += 7


def read_fields(buffer):
    position = 0
    while position < len(buffer):
        key, position = read_varint(buffer, position)
        field_number, wire_type = key >> 3, key & 7
        if wire_type == WIRE_VARINT:
            value, position = read_varint(buffer, position)
        elif wire_type == WIRE_LENGTH_DELIMITED:
            length, position = read_varint(buffer, position)
            value = buffer[position:position + length]
            position += length
        elif wire_type == WIRE_FIXED64:
            value = buffer[position:position + 8]
            position += 8
        elif wire_type == WIRE_FIXED32:
            value = buffer[position:position + 4]
            position += 4
        else:
            raise ValueError(f"unsupported wire type {wire_type}")
        yield field_number, value


def decode_any_value(buffer):
    for field_number, value in read_fields(buffer):
        if field_number == 1:
            return value.decode("utf-8", "replace")
        if field_number == 2:
            return bool(value)
        if field_number == 3:
            return value
    return None


def decode_attributes(raw_attributes):
    attributes = {}
    for raw in raw_attributes:
        key = ""
        value = None
        for field_number, content in read_fields(raw):
            if field_number == 1:
                key = content.decode("utf-8", "replace")
            elif field_number == 2:
                value = decode_any_value(content)
        attributes[key] = value
    return attributes


def decode_span(buffer, scope_name):
    span = {"scope": scope_name, "attributes": {}, "parentSpanId": "", "kind": 0}
    raw_attributes = []
    for field_number, value in read_fields(buffer):
        if field_number == 1:
            span["traceId"] = value.hex()
        elif field_number == 2:
            span["spanId"] = value.hex()
        elif field_number == 4:
            span["parentSpanId"] = value.hex()
        elif field_number == 5:
            span["name"] = value.decode("utf-8", "replace")
        elif field_number == 6:
            span["kind"] = value
        elif field_number == 9:
            raw_attributes.append(value)
    span["attributes"] = decode_attributes(raw_attributes)
    return span


def decode_scope_name(buffer):
    for field_number, value in read_fields(buffer):
        if field_number == 1:
            return value.decode("utf-8", "replace")
    return ""


def decode_scope_spans(buffer):
    scope_name = ""
    raw_spans = []
    for field_number, value in read_fields(buffer):
        if field_number == 1:
            scope_name = decode_scope_name(value)
        elif field_number == 2:
            raw_spans.append(value)
    return [decode_span(raw, scope_name) for raw in raw_spans]


def decode_resource_attributes(buffer):
    raw_attributes = [value for field_number, value in read_fields(buffer) if field_number == 1]
    return decode_attributes(raw_attributes)


def decode_export_request(buffer):
    spans = []
    for field_number, resource_spans in read_fields(buffer):
        if field_number != 1:
            continue
        resource_attributes = {}
        scope_spans_list = []
        for inner_number, content in read_fields(resource_spans):
            if inner_number == 1:
                resource_attributes = decode_resource_attributes(content)
            elif inner_number == 2:
                scope_spans_list.append(content)
        for scope_spans in scope_spans_list:
            for span in decode_scope_spans(scope_spans):
                span["resource"] = resource_attributes
                spans.append(span)
    return spans


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        return

    def reply(self, status, body=b"", content_type="application/json"):
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        if self.path == "/v1/traces":
            spans = decode_export_request(body)
            with received_lock:
                received_spans.extend(spans)
            self.reply(200, b"", "application/x-protobuf")
        elif self.path == "/reset":
            with received_lock:
                received_spans.clear()
            self.reply(200, b"{}")
        else:
            self.reply(404)

    def do_GET(self):
        if self.path == "/dump":
            with received_lock:
                body = json.dumps(received_spans, indent=1).encode("utf-8")
            self.reply(200, body)
        else:
            self.reply(404)


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 4318
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()


if __name__ == "__main__":
    main()
