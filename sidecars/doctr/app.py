"""Kontener HTTP dla docTR (Mindee) – tekst ze słowami + (od 1.2) układ strony i tabele, tylko CPU.

POST /ocr?filename=x.png  (body = PDF lub obraz) -> JSON w kontrakcie wspólnym dla silników (patrz README).
Dodatkowo ?raw=1 dołącza surowy eksport docTR (do diagnostyki).
"""

from __future__ import annotations

import os
import sys

sys.path.insert(0, "/srv")

from importlib.metadata import version as pkg_version  # noqa: E402

from common.imaging import load_pages  # noqa: E402
from common.ocr_http import json_response, serve  # noqa: E402
from common.tables import table_text  # noqa: E402


def _env_bool(name: str, default: bool) -> bool:
    return os.environ.get(name, str(default)).strip().lower() in ("1", "true", "yes")


# Wbudowane modele rozpoznawania docTR mają słownik "french" (bez ą, ę, ł, ś, ż, ź, ć, ń).
# Model wielojęzyczny od opiekunów docTR (HF Hub) zawiera wszystkie polskie znaki.
SETTINGS = {
    "det_arch": os.environ.get("DOCTR_DET_ARCH", "fast_base"),
    "reco": os.environ.get("DOCTR_RECO", "hub:Felix92/doctr-torch-parseq-multilingual-v1"),
    "detect_tables": _env_bool("DOCTR_DETECT_TABLES", True),
    "assume_straight_pages": _env_bool("DOCTR_ASSUME_STRAIGHT_PAGES", True),
}
PDF_DPI = int(os.environ.get("PDF_DPI", "300"))

_predictor = None


def predictor():
    global _predictor
    if _predictor is None:
        from doctr.models import from_hub, ocr_predictor

        reco = SETTINGS["reco"]
        reco_arch = from_hub(reco[4:]) if reco.startswith("hub:") else reco
        _predictor = ocr_predictor(
            det_arch=SETTINGS["det_arch"],
            reco_arch=reco_arch,
            pretrained=True,
            assume_straight_pages=SETTINGS["assume_straight_pages"],
            export_as_straight_boxes=True,
            detect_tables=SETTINGS["detect_tables"],
        )
    return _predictor


VERSION = f"docTR {pkg_version('python-doctr')} (torch {pkg_version('torch')})"


def health() -> dict:
    return {"status": "ok", "engine": "doctr", "version": VERSION, "details": {**SETTINGS, "pdf_dpi": PDF_DPI}}


def _box(geometry) -> list[float]:
    """Geometria docTR ((xmin, ymin), (xmax, ymax)) albo wielokąt – współrzędne względne 0..1."""
    xs = [p[0] for p in geometry]
    ys = [p[1] for p in geometry]
    return [round(min(xs), 6), round(min(ys), 6), round(max(xs), 6), round(max(ys), 6)]


def _convert_page(number: int, page: dict) -> dict:
    height, width = page["dimensions"]
    # Elementy strony (bloki tekstu i tabele) układane wg górnej krawędzi – kolejność czytania
    # dla tekstu strony i dla linii warstwy tekstowej PDF.
    items = []  # (górna krawędź, tekst, linie)
    for block in page.get("blocks", []):
        block_lines = []
        for line in block.get("lines", []):
            words = [w for w in line.get("words", []) if w.get("value", "").strip()]
            if not words:
                continue
            block_lines.append({
                "text": " ".join(w["value"] for w in words),
                "confidence": sum(w["confidence"] for w in words) / len(words),
                "box": _box(line["geometry"]),
                "words": [{"text": w["value"], "confidence": w["confidence"], "box": _box(w["geometry"])} for w in words],
            })
        if block_lines:
            items.append((_box(block["geometry"])[1], "\n".join(ln["text"] for ln in block_lines), block_lines))

    tables = []
    for t in page.get("tables", []):
        cells = sorted(
            ({
                "row": c["row_start"], "col": c["col_start"],
                "rowSpan": c["row_end"] - c["row_start"] + 1,
                "colSpan": c["col_end"] - c["col_start"] + 1,
                "text": c.get("value", ""), "box": _box(c["geometry"]),
                "confidence": c.get("confidence"),
            } for c in t.get("cells", [])),
            key=lambda c: (c["row"], c["col"]),
        )
        # Słowa z tabel docTR usuwa z bloków – komórki (wiersz po wierszu) trafiają do warstwy tekstowej jako linie.
        cell_lines = [{"text": c["text"], "confidence": c.pop("confidence"), "box": c["box"], "words": []}
                      for c in cells if c["text"].strip()]
        for c in cells:
            c.pop("confidence", None)
        box = _box(t["geometry"])
        tables.append({"box": box, "rows": t["num_rows"], "cols": t["num_cols"], "html": None, "cells": cells})
        items.append((box[1], table_text(t["num_rows"], cells), cell_lines))

    items.sort(key=lambda it: it[0])
    return {"number": number, "width": width, "height": height,
            "text": "\n".join(text for _, text, _ in items if text),
            "lines": [ln for _, _, item_lines in items for ln in item_lines],
            "tables": tables}


def ocr(data: bytes, query: dict[str, str]) -> tuple[str, bytes, dict[str, str]]:
    pages_rgb = load_pages(data, PDF_DPI)
    result = predictor()(pages_rgb).export()
    pages = [_convert_page(i, p) for i, p in enumerate(result["pages"], start=1)]
    payload = {"engine": "doctr", "version": VERSION, "pages": pages}
    if query.get("raw") == "1":
        payload["raw"] = result
    return json_response(payload)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--warmup":
        # Wywoływane w trakcie `docker build`: pobiera modele do obrazu.
        import numpy as np

        predictor()([np.full((512, 512, 3), 255, dtype=np.uint8)])
        print("warmup ok:", VERSION)
        sys.exit(0)
    predictor()
    serve(ocr, health, int(os.environ.get("PORT", "8000")))
