"""Kontener HTTP dla OCRmyPDF + Tesseract.

POST /ocr?filename=x.pdf  (body = PDF) -> JSON {"version", "pdf": <base64 PDF z warstwą tekstową>, "pages": [tekst]}.
Tekst stron pochodzi z --sidecar (kolejność czytania wyznaczona przez Tesseract).
Klient .NET zawsze wysyła PDF (obrazy są wcześniej zamieniane na PDF z zachowaniem DPI).
"""

from __future__ import annotations

import base64
import os
import shlex
import subprocess
import sys
import tempfile
from pathlib import Path

sys.path.insert(0, "/srv")
from common.ocr_http import json_response, serve  # noqa: E402

LANGUAGES = os.environ.get("OCR_LANGUAGES", "pol+eng")
JOBS = os.environ.get("OCRMYPDF_JOBS", str(os.cpu_count() or 1))
# Domyślnie: wymuś OCR każdej strony, zwykły PDF (bez konwersji PDF/A), automatyczny obrót stron.
EXTRA_ARGS = shlex.split(os.environ.get("OCRMYPDF_EXTRA_ARGS", "--force-ocr --output-type pdf --rotate-pages"))


def _version(cmd: list[str]) -> str:
    try:
        return subprocess.run(cmd, capture_output=True, text=True, timeout=30).stdout.splitlines()[0].strip()
    except Exception as exc:  # noqa: BLE001
        return f"unknown ({exc})"


VERSIONS = {
    "ocrmypdf": _version(["ocrmypdf", "--version"]),
    "tesseract": _version(["tesseract", "--version"]),
}


def health() -> dict:
    return {
        "status": "ok",
        "engine": "ocrmypdf",
        "version": f"OCRmyPDF {VERSIONS['ocrmypdf']} / {VERSIONS['tesseract']}",
        "details": {"languages": LANGUAGES, "jobs": JOBS, "args": EXTRA_ARGS},
    }


def ocr(data: bytes, query: dict[str, str]) -> tuple[str, bytes, dict[str, str]]:
    """Zwraca JSON: {"version", "pdf": base64, "pages": [tekst strony w kolejności czytania Tesseracta]}."""
    with tempfile.TemporaryDirectory() as tmp:
        src = Path(tmp) / "input.pdf"
        dst = Path(tmp) / "output.pdf"
        txt = Path(tmp) / "output.txt"
        src.write_bytes(data)
        cmd = ["ocrmypdf", "-l", LANGUAGES, "--jobs", JOBS, "--sidecar", str(txt), *EXTRA_ARGS, str(src), str(dst)]
        proc = subprocess.run(cmd, capture_output=True, text=True)
        if proc.returncode != 0:
            raise RuntimeError(f"ocrmypdf exit {proc.returncode}: {proc.stderr[-4000:]}")
        # Plik --sidecar: tekst stron rozdzielony znakiem form feed (\f).
        pages = txt.read_text(encoding="utf-8").split("\f") if txt.exists() else []
        if len(pages) > 1 and not pages[-1].strip():
            pages = pages[:-1]  # separator po ostatniej stronie
        payload = {
            "version": health()["version"],
            "pdf": base64.b64encode(dst.read_bytes()).decode("ascii"),
            "pages": [p.strip() for p in pages],
        }
        return json_response(payload)


if __name__ == "__main__":
    serve(ocr, health, int(os.environ.get("PORT", "8000")))
