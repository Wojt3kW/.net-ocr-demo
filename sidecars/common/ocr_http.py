"""Minimalny serwer HTTP dla kontenerów OCR (tylko biblioteka standardowa).

Kontrakt:
  GET  /health                      -> 200 {"status": "ok", "engine": ..., "version": ..., "details": {...}}
  POST /ocr?filename=<nazwa pliku>  -> body = surowe bajty pliku (PDF / obraz)
                                       odpowiedź zależy od silnika (JSON lub PDF)

Żądania OCR są serializowane (jeden dokument naraz), bo modele nie są bezpieczne
wielowątkowo, a pomiar czasu ma być porównywalny między silnikami.
"""

from __future__ import annotations

import json
import logging
import threading
import time
import traceback
from collections.abc import Callable
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, urlparse

# handler(data, query) -> (content_type, body, extra_headers)
OcrHandler = Callable[[bytes, dict[str, str]], tuple[str, bytes, dict[str, str]]]
HealthHandler = Callable[[], dict]

log = logging.getLogger("ocr_http")


def json_response(payload: dict) -> tuple[str, bytes, dict[str, str]]:
    return "application/json; charset=utf-8", json.dumps(payload, ensure_ascii=False).encode("utf-8"), {}


def serve(ocr: OcrHandler, health: HealthHandler, port: int = 8000) -> None:
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(name)s: %(message)s")
    lock = threading.Lock()

    class Handler(BaseHTTPRequestHandler):
        protocol_version = "HTTP/1.1"

        def log_message(self, fmt: str, *args) -> None:  # noqa: N802 - API biblioteki standardowej
            log.info("%s - %s", self.address_string(), fmt % args)

        def _send(self, status: int, content_type: str, body: bytes, headers: dict[str, str] | None = None) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            for k, v in (headers or {}).items():
                self.send_header(k, v)
            self.end_headers()
            self.wfile.write(body)

        def _json(self, status: int, payload: dict) -> None:
            ctype, body, _ = json_response(payload)
            self._send(status, ctype, body)

        def do_GET(self) -> None:  # noqa: N802
            if urlparse(self.path).path == "/health":
                self._json(200, health())
            else:
                self._json(404, {"error": "not found"})

        def do_POST(self) -> None:  # noqa: N802
            url = urlparse(self.path)
            if url.path != "/ocr":
                self._json(404, {"error": "not found"})
                return
            query = {k: v[0] for k, v in parse_qs(url.query).items()}
            length = int(self.headers.get("Content-Length") or 0)
            data = self.rfile.read(length) if length > 0 else b""
            if not data:
                self._json(400, {"error": "empty body"})
                return
            try:
                with lock:
                    started = time.perf_counter()
                    ctype, body, headers = ocr(data, query)
                    elapsed_ms = int((time.perf_counter() - started) * 1000)
                headers = {**headers, "X-Processing-Time-Ms": str(elapsed_ms)}
                log.info("OCR %s: %d B -> %d B w %d ms", query.get("filename", "?"), len(data), len(body), elapsed_ms)
                self._send(200, ctype, body, headers)
            except Exception as exc:  # noqa: BLE001 - błąd ma trafić do klienta
                log.exception("OCR failed")
                self._json(500, {"error": f"{type(exc).__name__}: {exc}", "trace": traceback.format_exc()})

    server = ThreadingHTTPServer(("0.0.0.0", port), Handler)
    log.info("Listening on :%d", port)
    server.serve_forever()
