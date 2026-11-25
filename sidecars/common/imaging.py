"""Zamiana pliku wejściowego (PDF / obraz, także wielostronicowy TIFF) na listę stron RGB."""

from __future__ import annotations

import io

import numpy as np
from PIL import Image, ImageSequence


def load_pages(data: bytes, pdf_dpi: int = 300) -> list[np.ndarray]:
    """Zwraca strony jako tablice numpy RGB (H, W, 3) uint8.

    PDF jest rasteryzowany przez pdfium w zadanej rozdzielczości (domyślnie 300 DPI),
    obrazy są wczytywane w natywnej rozdzielczości (wszystkie ramki TIFF).
    """
    if data[:5] == b"%PDF-":
        import pypdfium2 as pdfium

        pdf = pdfium.PdfDocument(data)
        try:
            pages = []
            for page in pdf:
                bitmap = page.render(scale=pdf_dpi / 72.0)
                pages.append(np.asarray(bitmap.to_pil().convert("RGB")))
                page.close()
            return pages
        finally:
            pdf.close()

    with Image.open(io.BytesIO(data)) as img:
        return [np.asarray(frame.convert("RGB")) for frame in ImageSequence.Iterator(img)]


def norm_box(x0: float, y0: float, x1: float, y1: float, width: float, height: float) -> list[float]:
    """Ramka w pikselach -> ramka znormalizowana 0..1 (początek układu: lewy górny róg)."""
    return [
        round(max(0.0, min(1.0, x0 / width)), 6),
        round(max(0.0, min(1.0, y0 / height)), 6),
        round(max(0.0, min(1.0, x1 / width)), 6),
        round(max(0.0, min(1.0, y1 / height)), 6),
    ]
