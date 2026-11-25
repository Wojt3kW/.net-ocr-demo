using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using OcrDemo.Core.Model;

namespace OcrDemo.Runner;

/// <summary>Zapisuje wyniki jednego silnika dla jednego dokumentu (tekst, JSON, PDF, tabele).</summary>
public static class ResultWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // polskie znaki czytelne w pliku
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    public static async Task WriteAsync(string directory, OcrDocumentResult result, byte[] searchablePdf, bool saveRaw, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        var prefix = Path.Combine(directory, result.Engine);

        await File.WriteAllTextAsync($"{prefix}.txt", result.Text, Encoding.UTF8, ct);
        await File.WriteAllBytesAsync($"{prefix}.pdf", searchablePdf, ct);
        await File.WriteAllTextAsync($"{prefix}.json", JsonSerializer.Serialize(new
        {
            result.Engine,
            result.EngineVersion,
            DurationMs = (long)result.Duration.TotalMilliseconds,
            NativeTextLayer = result.NativeSearchablePdf is not null,
            result.Pages,
        }, JsonOptions), Encoding.UTF8, ct);

        if (saveRaw && result.RawJson is not null)
        {
            await File.WriteAllTextAsync($"{prefix}.raw.json", result.RawJson, Encoding.UTF8, ct);
        }

        var tables = result.Tables.ToList();
        if (tables.Count == 0)
        {
            return;
        }

        await File.WriteAllTextAsync($"{prefix}.tables.html", TablesToHtml(result.Engine, tables), Encoding.UTF8, ct);
        for (var i = 0; i < tables.Count; i++)
        {
            // CSV ze średnikiem i BOM – otwiera się poprawnie w polskim Excelu.
            await File.WriteAllTextAsync($"{prefix}.table-{i + 1}.csv", TableToCsv(tables[i]), Utf8Bom, ct);
        }
    }

    public static Task WriteErrorAsync(string directory, string engine, Exception error, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        return File.WriteAllTextAsync(Path.Combine(directory, $"{engine}.error.txt"), error.ToString(), Encoding.UTF8, ct);
    }

    public static string TableToCsv(OcrTable table)
    {
        var sb = new StringBuilder();
        foreach (var row in table.ToGrid())
        {
            sb.AppendLine(string.Join(";", row.Select(Escape)));
        }

        return sb.ToString();

        static string Escape(string value) =>
            value.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    public static string TablesToHtml(string engine, IReadOnlyList<OcrTable> tables)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"pl\"><head><meta charset=\"utf-8\">");
        sb.AppendLine(CultureInfo.InvariantCulture, $"<title>Tabele – {WebUtility.HtmlEncode(engine)}</title>");
        sb.AppendLine("<style>body{font-family:sans-serif;margin:16px}table{border-collapse:collapse;margin-bottom:24px}td{border:1px solid #888;padding:4px 8px;vertical-align:top}caption{text-align:left;font-weight:bold;padding:4px 0}</style>");
        sb.AppendLine("</head><body>");
        for (var i = 0; i < tables.Count; i++)
        {
            var table = tables[i];
            sb.AppendLine(CultureInfo.InvariantCulture, $"<table><caption>Tabela {i + 1} (strona {table.PageNumber}, {table.Rows}×{table.Cols})</caption>");
            for (var r = 0; r < table.Rows; r++)
            {
                sb.Append("<tr>");
                foreach (var cell in table.Cells.Where(c => c.Row == r).DistinctBy(c => c.Col).OrderBy(c => c.Col))
                {
                    var span = (cell.RowSpan > 1 ? $" rowspan=\"{cell.RowSpan}\"" : string.Empty)
                             + (cell.ColSpan > 1 ? $" colspan=\"{cell.ColSpan}\"" : string.Empty);
                    sb.Append(CultureInfo.InvariantCulture, $"<td{span}>{WebUtility.HtmlEncode(cell.Text)}</td>");
                }

                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table>");
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
