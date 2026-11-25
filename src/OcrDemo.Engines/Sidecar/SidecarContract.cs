using System.Text.Json;
using System.Text.Json.Serialization;
using OcrDemo.Core.Model;

namespace OcrDemo.Engines.Sidecar;

// Wspólny kontrakt JSON kontenerów Python (sidecars/common, paddleocr, doctr).
// Ramki: [x0, y0, x1, y1] znormalizowane 0..1, początek w lewym górnym rogu.

internal sealed record SidecarHealth(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("engine")] string Engine,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("details")] JsonElement? Details);

internal sealed record SidecarResponse(
    [property: JsonPropertyName("engine")] string Engine,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("pages")] List<SidecarPage> Pages);

internal sealed record SidecarPage(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("width")] double Width,
    [property: JsonPropertyName("height")] double Height,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("lines")] List<SidecarLine> Lines,
    [property: JsonPropertyName("tables")] List<SidecarTable> Tables);

internal sealed record SidecarLine(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("box")] double[] Box,
    [property: JsonPropertyName("words")] List<SidecarWord>? Words);

internal sealed record SidecarWord(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("confidence")] double? Confidence,
    [property: JsonPropertyName("box")] double[] Box);

internal sealed record SidecarTable(
    [property: JsonPropertyName("box")] double[]? Box,
    [property: JsonPropertyName("rows")] int Rows,
    [property: JsonPropertyName("cols")] int Cols,
    [property: JsonPropertyName("html")] string? Html,
    [property: JsonPropertyName("cells")] List<SidecarCell> Cells);

internal sealed record SidecarCell(
    [property: JsonPropertyName("row")] int Row,
    [property: JsonPropertyName("col")] int Col,
    [property: JsonPropertyName("rowSpan")] int RowSpan,
    [property: JsonPropertyName("colSpan")] int ColSpan,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("box")] double[]? Box);

internal static class SidecarMapping
{
    public static NormBox ToBox(double[] box) => new(box[0], box[1], box[2], box[3]);

    public static NormBox? ToBoxOrNull(double[]? box) => box is { Length: 4 } ? ToBox(box) : null;

    public static OcrPage ToPage(SidecarPage page) => new(
        page.Number,
        page.Text,
        page.Lines
            .Where(l => l.Box is { Length: 4 })
            .Select(l => new OcrLine(
                l.Text,
                ToBox(l.Box),
                l.Confidence,
                (l.Words ?? []).Where(w => w.Box is { Length: 4 }).Select(w => new OcrWord(w.Text, ToBox(w.Box), w.Confidence)).ToList()))
            .ToList(),
        page.Tables
            .Select(t => new OcrTable(
                page.Number,
                ToBoxOrNull(t.Box),
                t.Rows,
                t.Cols,
                t.Cells.Select(c => new OcrTableCell(c.Row, c.Col, c.RowSpan, c.ColSpan, c.Text, ToBoxOrNull(c.Box))).ToList(),
                t.Html))
            .ToList());
}
