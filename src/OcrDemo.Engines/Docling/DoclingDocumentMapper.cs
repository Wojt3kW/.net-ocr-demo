using System.Text;
using System.Text.Json;
using OcrDemo.Core.Model;

namespace OcrDemo.Engines.Docling;

/// <summary>
/// Zamiana DoclingDocument (json_content z docling-serve) na wspólny model.
/// Kolejność czytania = kolejność drzewa <c>body</c> (teksty, grupy, tabele, obrazy z tekstem).
/// </summary>
/// <remarks>
/// Docling zwraca ramki akapitów, a nie słów. Do warstwy tekstowej akapit jest dzielony na linie
/// (szacowanie liczby linii z proporcji ramki i długości tekstu) – położenie tekstu jest przybliżone.
/// </remarks>
public static class DoclingDocumentMapper
{
    public static IReadOnlyList<OcrPage> Map(JsonElement doc)
    {
        var pageSizes = new Dictionary<int, (double W, double H)>();
        foreach (var page in doc.GetProperty("pages").EnumerateObject())
        {
            var size = page.Value.GetProperty("size");
            pageSizes[int.Parse(page.Name)] = (size.GetProperty("width").GetDouble(), size.GetProperty("height").GetDouble());
        }

        var builders = pageSizes.Keys.Order().ToDictionary(n => n, n => new PageBuilder(n));
        var visited = new HashSet<string>();

        void Visit(string reference)
        {
            if (!visited.Add(reference) || !TryResolve(doc, reference, out var item))
            {
                return;
            }

            if (reference.StartsWith("#/texts/", StringComparison.Ordinal))
            {
                AddText(item, pageSizes, builders);
            }
            else if (reference.StartsWith("#/tables/", StringComparison.Ordinal))
            {
                AddTable(item, pageSizes, builders);
                return; // komórki tabeli zawierają już jej tekst
            }

            if (item.TryGetProperty("children", out var children))
            {
                foreach (var child in children.EnumerateArray())
                {
                    Visit(child.GetProperty("$ref").GetString()!);
                }
            }
        }

        Visit("#/body");
        return builders.Values.Select(b => b.Build()).ToList();
    }

    private static void AddText(JsonElement item, Dictionary<int, (double W, double H)> sizes, Dictionary<int, PageBuilder> builders)
    {
        var text = item.TryGetProperty("text", out var t) ? t.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrWhiteSpace(text) || !TryGetProv(item, sizes, out var pageNo, out var box))
        {
            return;
        }

        var page = builders[pageNo];
        page.Text.AppendLine(text);
        foreach (var line in SplitIntoLines(text, box!.Value, sizes[pageNo]))
        {
            page.Lines.Add(line);
        }
    }

    private static void AddTable(JsonElement item, Dictionary<int, (double W, double H)> sizes, Dictionary<int, PageBuilder> builders)
    {
        if (!TryGetProv(item, sizes, out var pageNo, out var tableBox))
        {
            return;
        }

        var data = item.GetProperty("data");
        var size = sizes[pageNo];
        var cells = new List<OcrTableCell>();
        foreach (var cell in data.GetProperty("table_cells").EnumerateArray())
        {
            var text = cell.GetProperty("text").GetString() ?? string.Empty;
            NormBox? box = cell.TryGetProperty("bbox", out var bbox) && bbox.ValueKind == JsonValueKind.Object ? ToNorm(bbox, size) : null;
            cells.Add(new OcrTableCell(
                cell.GetProperty("start_row_offset_idx").GetInt32(),
                cell.GetProperty("start_col_offset_idx").GetInt32(),
                cell.GetProperty("row_span").GetInt32(),
                cell.GetProperty("col_span").GetInt32(),
                text,
                box));
        }

        var table = new OcrTable(pageNo, tableBox, data.GetProperty("num_rows").GetInt32(), data.GetProperty("num_cols").GetInt32(), cells, null);
        var page = builders[pageNo];
        page.Tables.Add(table);

        // Tekst tabeli wiersz po wierszu + komórki jako linie warstwy tekstowej.
        foreach (var row in table.ToGrid())
        {
            var rowText = string.Join(" ", row.Where(c => c.Length > 0));
            if (rowText.Length > 0)
            {
                page.Text.AppendLine(rowText);
            }
        }

        // Komórki scalone występują w table_cells raz – deduplikacja po pozycji.
        foreach (var cell in cells.Where(c => c.Box is not null && c.Text.Length > 0).DistinctBy(c => (c.Row, c.Col)))
        {
            page.Lines.Add(new OcrLine(cell.Text, cell.Box!.Value, null, []));
        }
    }

    private static bool TryGetProv(JsonElement item, Dictionary<int, (double W, double H)> sizes, out int pageNo, out NormBox? box)
    {
        pageNo = 0;
        box = null;
        if (!item.TryGetProperty("prov", out var prov) || prov.GetArrayLength() == 0)
        {
            return false;
        }

        var first = prov[0];
        pageNo = first.GetProperty("page_no").GetInt32();
        if (!sizes.TryGetValue(pageNo, out var size))
        {
            return false;
        }

        box = ToNorm(first.GetProperty("bbox"), size);
        return true;
    }

    /// <summary>bbox Docling: l, t, r, b + coord_origin (BOTTOMLEFT dla tekstów, TOPLEFT dla komórek tabel).</summary>
    internal static NormBox ToNorm(JsonElement bbox, (double W, double H) size)
    {
        var l = bbox.GetProperty("l").GetDouble();
        var t = bbox.GetProperty("t").GetDouble();
        var r = bbox.GetProperty("r").GetDouble();
        var b = bbox.GetProperty("b").GetDouble();
        var origin = bbox.TryGetProperty("coord_origin", out var o) ? o.GetString() : "TOPLEFT";
        if (string.Equals(origin, "BOTTOMLEFT", StringComparison.OrdinalIgnoreCase))
        {
            (t, b) = (size.H - t, size.H - b);
        }

        return NormBox.FromPixels(l, Math.Min(t, b), r, Math.Max(t, b), size.W, size.H);
    }

    /// <summary>
    /// Szacuje liczbę linii akapitu: przy szerokości znaku ≈ 0,5 wysokości linii
    /// n² ≈ 0,5 · liczba_znaków · wysokość / szerokość. Tekst dzielony po słowach na n równych pasów.
    /// </summary>
    internal static IEnumerable<OcrLine> SplitIntoLines(string text, NormBox box, (double W, double H) size)
    {
        var widthPt = box.Width * size.W;
        var heightPt = box.Height * size.H;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || widthPt <= 0 || heightPt <= 0)
        {
            yield break;
        }

        var lineCount = (int)Math.Clamp(Math.Round(Math.Sqrt(0.5 * text.Length * heightPt / widthPt)), 1, words.Length);
        var targetChars = (double)text.Length / lineCount;
        var lineHeight = box.Height / lineCount;
        var current = new List<string>();
        var index = 0;
        for (var i = 0; i < words.Length; i++)
        {
            current.Add(words[i]);
            var remainingWords = words.Length - i - 1;
            var remainingLines = lineCount - index - 1;
            var full = string.Join(' ', current).Length >= targetChars && remainingLines > 0;
            if (full || remainingWords < remainingLines || i == words.Length - 1)
            {
                var y0 = box.Y0 + index * lineHeight;
                yield return new OcrLine(string.Join(' ', current), new NormBox(box.X0, y0, box.X1, y0 + lineHeight), null, []);
                current.Clear();
                index++;
            }
        }
    }

    private static bool TryResolve(JsonElement doc, string reference, out JsonElement item)
    {
        item = default;
        var parts = reference.TrimStart('#', '/').Split('/');
        var current = doc;
        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index) && index < current.GetArrayLength())
            {
                current = current[index];
            }
            else if (current.ValueKind == JsonValueKind.Object && current.TryGetProperty(part, out var next))
            {
                current = next;
            }
            else
            {
                return false;
            }
        }

        item = current;
        return true;
    }

    private sealed class PageBuilder(int number)
    {
        public StringBuilder Text { get; } = new();
        public List<OcrLine> Lines { get; } = [];
        public List<OcrTable> Tables { get; } = [];

        public OcrPage Build() => new(number, Text.ToString().TrimEnd(), Lines, Tables);
    }
}
