using System.Text.Json;
using OcrDemo.Core.Model;
using OcrDemo.Engines.Docling;

namespace OcrDemo.Tests;

public class DoclingDocumentMapperTests
{
    // Fragment rzeczywistej odpowiedzi docling-serve 1.39 (json_content), skrócony.
    private const string Json = """
    {
      "body": { "children": [ { "$ref": "#/texts/0" }, { "$ref": "#/groups/0" }, { "$ref": "#/tables/0" } ] },
      "groups": [ { "children": [ { "$ref": "#/texts/1" } ] } ],
      "texts": [
        { "label": "section_header", "text": "Faktura VAT",
          "prov": [ { "page_no": 1, "bbox": { "l": 50, "t": 800, "r": 250, "b": 780, "coord_origin": "BOTTOMLEFT" } } ] },
        { "label": "text", "text": "Zażółć gęślą jaźń",
          "prov": [ { "page_no": 1, "bbox": { "l": 50, "t": 700, "r": 300, "b": 688, "coord_origin": "BOTTOMLEFT" } } ] }
      ],
      "tables": [
        { "prov": [ { "page_no": 1, "bbox": { "l": 40, "t": 600, "r": 500, "b": 500, "coord_origin": "BOTTOMLEFT" } } ],
          "data": { "num_rows": 2, "num_cols": 2, "table_cells": [
            { "bbox": { "l": 45, "t": 250, "r": 100, "b": 265, "coord_origin": "TOPLEFT" }, "row_span": 1, "col_span": 1,
              "start_row_offset_idx": 0, "start_col_offset_idx": 0, "text": "Lp." },
            { "bbox": { "l": 120, "t": 250, "r": 200, "b": 265, "coord_origin": "TOPLEFT" }, "row_span": 1, "col_span": 1,
              "start_row_offset_idx": 0, "start_col_offset_idx": 1, "text": "Nazwa" },
            { "bbox": { "l": 45, "t": 270, "r": 100, "b": 285, "coord_origin": "TOPLEFT" }, "row_span": 1, "col_span": 1,
              "start_row_offset_idx": 1, "start_col_offset_idx": 0, "text": "1" },
            { "bbox": { "l": 120, "t": 270, "r": 200, "b": 285, "coord_origin": "TOPLEFT" }, "row_span": 1, "col_span": 1,
              "start_row_offset_idx": 1, "start_col_offset_idx": 1, "text": "Śruby" }
          ] } }
      ],
      "pages": { "1": { "size": { "width": 600, "height": 800 }, "page_no": 1 } }
    }
    """;

    [Fact]
    public void Map_ProducesReadingOrderTextTablesAndNormalizedBoxes()
    {
        using var doc = JsonDocument.Parse(Json);

        var page = Assert.Single(DoclingDocumentMapper.Map(doc.RootElement));

        Assert.Equal("Faktura VAT\nZażółć gęślą jaźń\nLp. Nazwa\n1 Śruby", page.Text.ReplaceLineEndings("\n"));
        var table = Assert.Single(page.Tables);
        Assert.Equal(["Lp.", "Nazwa"], table.ToGrid()[0]);
        Assert.Equal(["1", "Śruby"], table.ToGrid()[1]);

        // BOTTOMLEFT: t=800 -> y0=0 (góra strony)
        var header = page.Lines.First(l => l.Text == "Faktura VAT");
        Assert.Equal(new NormBox(50d / 600, 0, 250d / 600, 20d / 800), header.Box);

        // TOPLEFT (komórki tabel) bez odwracania osi Y
        var cell = page.Lines.First(l => l.Text == "Śruby");
        Assert.Equal(270d / 800, cell.Box.Y0, 6);
    }

    [Fact]
    public void SplitIntoLines_SplitsTallParagraphIntoSeveralLines()
    {
        // Akapit ~3 linii: szeroki na 500 pt, wysoki na 45 pt.
        var text = string.Join(' ', Enumerable.Repeat("słowo", 40));
        var box = new NormBox(0.1, 0.1, 0.1 + 500d / 600, 0.1 + 45d / 800);

        var lines = DoclingDocumentMapper.SplitIntoLines(text, box, (600, 800)).ToList();

        Assert.InRange(lines.Count, 2, 5);
        Assert.Equal(text, string.Join(' ', lines.Select(l => l.Text)));
        Assert.All(lines, l => Assert.True(l.Box.Y0 >= box.Y0 && l.Box.Y1 <= box.Y1 + 1e-9));
    }
}
