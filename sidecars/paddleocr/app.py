"""Kontener HTTP dla PaddleOCR PP-StructureV3 (tekst + układ strony + tabele), tylko CPU.

POST /ocr?filename=x.png  (body = PDF lub obraz) -> JSON w kontrakcie wspólnym dla silników (patrz README).
Dodatkowo ?raw=1 dołącza surowy wynik PaddleOCR (do diagnostyki).
"""

from __future__ import annotations

import os
import sys

sys.path.insert(0, "/srv")

from importlib.metadata import version as pkg_version  # noqa: E402

from common.imaging import load_pages, norm_box  # noqa: E402
from common.ocr_http import json_response, serve  # noqa: E402
from common.tables import parse_html_table, table_text  # noqa: E402


def _env_bool(name: str, default: bool) -> bool:
    return os.environ.get(name, str(default)).strip().lower() in ("1", "true", "yes")


# PP-OCRv6 obsługuje jednym modelem chiński, angielski i języki łacińskie (w tym polski).
# Uwaga: PPStructureV3(lang="pl") w PaddleOCR 3.7 wybrałby starszy latin_PP-OCRv5_mobile_rec,
# dlatego nazwy modeli podajemy jawnie.
SETTINGS = {
    "text_detection_model_name": os.environ.get("PADDLE_DET_MODEL", "PP-OCRv6_medium_det"),
    "text_recognition_model_name": os.environ.get("PADDLE_REC_MODEL", "PP-OCRv6_medium_rec"),
    # Prostowanie/obracanie strony zmienia geometrię obrazu -> współrzędne nie pasowałyby do oryginału.
    "use_doc_orientation_classify": _env_bool("PADDLE_DOC_ORIENTATION", False),
    "use_doc_unwarping": False,
    "use_textline_orientation": _env_bool("PADDLE_TEXTLINE_ORIENTATION", True),
    "use_table_recognition": True,
    "use_seal_recognition": _env_bool("PADDLE_SEAL", False),
    "use_formula_recognition": _env_bool("PADDLE_FORMULA", False),
    "use_chart_recognition": False,
    "device": "cpu",
    # PaddlePaddle 3.3.1 + oneDNN: model układu strony kończy się błędem
    # "ConvertPirAttribute2RuntimeAttribute not support [pir::ArrayAttribute<pir::DoubleAttribute>]".
    # Bez MKL-DNN działa poprawnie (wolniej). Można włączyć PADDLE_ENABLE_MKLDNN=true po aktualizacji Paddle.
    "enable_mkldnn": _env_bool("PADDLE_ENABLE_MKLDNN", False),
    "cpu_threads": int(os.environ.get("PADDLE_CPU_THREADS", str(os.cpu_count() or 1))),
}
PDF_DPI = int(os.environ.get("PDF_DPI", "300"))

_pipeline = None


def pipeline():
    global _pipeline
    if _pipeline is None:
        from paddleocr import PPStructureV3

        _pipeline = PPStructureV3(**SETTINGS)
    return _pipeline


VERSION = f"PaddleOCR {pkg_version('paddleocr')} (PaddleX {pkg_version('paddlex')}, Paddle {pkg_version('paddlepaddle')})"


def health() -> dict:
    return {"status": "ok", "engine": "paddleocr", "version": VERSION,
            "details": {**SETTINGS, "pdf_dpi": PDF_DPI, "pipeline": "PP-StructureV3"}}


def _box_from_poly(poly) -> tuple[float, float, float, float]:
    xs = [p[0] for p in poly]
    ys = [p[1] for p in poly]
    return min(xs), min(ys), max(xs), max(ys)


def _convert_page(number: int, res: dict, width: int, height: int) -> dict:
    ocr = res.get("overall_ocr_res") or {}
    texts = ocr.get("rec_texts") or []
    scores = ocr.get("rec_scores") or []
    boxes = ocr.get("rec_boxes") or []
    polys = ocr.get("rec_polys") or []
    lines = []
    for i, text in enumerate(texts):
        if not text or not text.strip():
            continue
        if i < len(boxes) and len(boxes[i]) == 4:
            x0, y0, x1, y1 = boxes[i]
        elif i < len(polys):
            x0, y0, x1, y1 = _box_from_poly(polys[i])
        else:
            continue
        lines.append({
            "text": text,
            "confidence": float(scores[i]) if i < len(scores) else None,
            "box": norm_box(x0, y0, x1, y1, width, height),
            "words": [],  # PP-StructureV3 zwraca linie, nie pojedyncze słowa
        })

    # Tabele: bloki układu z etykietą "table" (kolejność zgodna z table_res_list).
    blocks = res.get("parsing_res_list") or []
    table_blocks = [b for b in blocks if b.get("block_label") == "table"]
    tables = []
    for i, t in enumerate(res.get("table_res_list") or []):
        html = t.get("pred_html") or ""
        n_rows, n_cols, cells = parse_html_table(html)
        box = None
        if i < len(table_blocks) and table_blocks[i].get("block_bbox"):
            x0, y0, x1, y1 = table_blocks[i]["block_bbox"]
            box = norm_box(x0, y0, x1, y1, width, height)
        tables.append({"box": box, "rows": n_rows, "cols": n_cols, "html": html, "cells": cells})

    # Tekst w kolejności czytania wyznaczonej przez model układu strony.
    parts = []
    for b in blocks:
        content = (b.get("block_content") or "").strip()
        if not content:
            continue
        if b.get("block_label") == "table":
            rows, _, cells = parse_html_table(content)
            content = table_text(rows, cells)
        parts.append(content)
    page_text = "\n".join(parts) if parts else "\n".join(line["text"] for line in lines)

    return {"number": number, "width": width, "height": height, "text": page_text, "lines": lines, "tables": tables}


def ocr(data: bytes, query: dict[str, str]) -> tuple[str, bytes, dict[str, str]]:
    pages_rgb = load_pages(data, PDF_DPI)
    pages = []
    raw = []
    for idx, rgb in enumerate(pages_rgb, start=1):
        bgr = rgb[:, :, ::-1].copy()  # PaddleX oczekuje BGR (konwencja OpenCV)
        results = list(pipeline().predict(bgr))
        res = results[0].json.get("res", {})
        height, width = rgb.shape[:2]
        pages.append(_convert_page(idx, res, width, height))
        if query.get("raw") == "1":
            raw.append(res)
    payload = {"engine": "paddleocr", "version": VERSION, "pages": pages}
    if raw:
        payload["raw"] = raw
    return json_response(payload)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--warmup":
        # Wywoływane w trakcie `docker build`: pobiera modele do obrazu.
        import numpy as np

        list(pipeline().predict(np.full((256, 256, 3), 255, dtype=np.uint8)))
        print("warmup ok:", VERSION)
        sys.exit(0)
    pipeline()  # ładowanie modeli przed przyjęciem pierwszego żądania
    serve(ocr, health, int(os.environ.get("PORT", "8000")))
