import http.server
import os
import sys

root = sys.argv[1]
port = int(sys.argv[2])


class SpaHandler(http.server.SimpleHTTPRequestHandler):
    def __init__(self, *args, **kwargs):
        super().__init__(*args, directory=root, **kwargs)

    def send_head(self):
        path = self.translate_path(self.path.split("?", 1)[0])
        if not os.path.exists(path) and "." not in os.path.basename(path):
            self.path = "/index.html"
        return super().send_head()

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()


http.server.ThreadingHTTPServer(("127.0.0.1", port), SpaHandler).serve_forever()
