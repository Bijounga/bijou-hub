"""A stand-in for an Elgato Key Light's local HTTP API, for testing BijouHub.

    python tools/fake_keylight.py 19123

Then give BijouHub the address 127.0.0.1:19123 (Key Light setup, or "KeyLightAddress" in
settings.json). Implements what BijouHub calls:
    GET /elgato/accessory-info   the light's name
    GET /elgato/lights           {"numberOfLights":1,"lights":[{"on":0|1,...}]}
    PUT /elgato/lights           sets "on" (and any other field sent)
    GET /_log                    every PUT received, in order, as JSON (test-only)
"""
import json
import sys
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

LIGHT = {"on": 0, "brightness": 20, "temperature": 213}
LOG = []


class Handler(BaseHTTPRequestHandler):
    def _send(self, body, status=200):
        data = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path == "/elgato/accessory-info":
            self._send({"productName": "Elgato Key Light Air", "displayName": "Test Key Light", "serialNumber": "TEST0001"})
        elif self.path == "/elgato/lights":
            self._send({"numberOfLights": 1, "lights": [LIGHT]})
        elif self.path == "/_log":
            self._send(LOG)
        else:
            self._send({}, 404)

    def do_PUT(self):
        if self.path != "/elgato/lights":
            self._send({}, 404)
            return
        length = int(self.headers.get("Content-Length", 0))
        body = json.loads(self.rfile.read(length) or b"{}")
        for light in body.get("lights", []):
            LIGHT.update(light)
        LOG.append({"on": LIGHT["on"]})
        self._send({"numberOfLights": 1, "lights": [LIGHT]})

    def log_message(self, *args):
        pass


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 9123
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
