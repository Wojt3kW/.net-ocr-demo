"""Parsowanie tabeli HTML (<table><tr><td rowspan colspan>) do listy komórek z pozycją w siatce."""

from __future__ import annotations

from html.parser import HTMLParser


class _TableParser(HTMLParser):
    def __init__(self) -> None:
        super().__init__(convert_charrefs=True)
        self.rows: list[list[dict]] = []
        self._cell: dict | None = None

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "tr":
            self.rows.append([])
        elif tag in ("td", "th"):
            a = dict(attrs)
            if not self.rows:
                self.rows.append([])
            self._cell = {
                "rowSpan": max(1, int(a.get("rowspan") or 1)),
                "colSpan": max(1, int(a.get("colspan") or 1)),
                "text": "",
            }

    def handle_endtag(self, tag: str) -> None:
        if tag in ("td", "th") and self._cell is not None:
            self._cell["text"] = " ".join(self._cell["text"].split())
            self.rows[-1].append(self._cell)
            self._cell = None

    def handle_data(self, data: str) -> None:
        if self._cell is not None:
            self._cell["text"] += data


def parse_html_table(html: str) -> tuple[int, int, list[dict]]:
    """Zwraca (liczba_wierszy, liczba_kolumn, komórki[{row, col, rowSpan, colSpan, text}])."""
    parser = _TableParser()
    parser.feed(html or "")
    occupied: set[tuple[int, int]] = set()
    cells: list[dict] = []
    n_cols = 0
    for r, row in enumerate(parser.rows):
        c = 0
        for cell in row:
            while (r, c) in occupied:
                c += 1
            for dr in range(cell["rowSpan"]):
                for dc in range(cell["colSpan"]):
                    occupied.add((r + dr, c + dc))
            cells.append({"row": r, "col": c, **cell, "box": None})
            c += cell["colSpan"]
            n_cols = max(n_cols, c)
    n_rows = max([r + 1 for r, _ in occupied], default=0)
    return n_rows, n_cols, cells


def table_text(n_rows: int, cells: list[dict]) -> str:
    """Tekst tabeli: wiersz po wierszu, komórki rozdzielone spacją."""
    lines = []
    for r in range(n_rows):
        row = sorted((c for c in cells if c["row"] == r), key=lambda c: c["col"])
        lines.append(" ".join(c["text"] for c in row if c["text"]))
    return "\n".join(line for line in lines if line)
