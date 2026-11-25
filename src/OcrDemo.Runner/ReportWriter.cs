using System.Globalization;
using System.Text;
using OcrDemo.Core;
using OcrDemo.Core.Metrics;

namespace OcrDemo.Runner;

public sealed record EngineRun(string Engine, EngineStatus Status);

/// <summary>Wynik jednego silnika dla jednego dokumentu.</summary>
public sealed record DocumentRun(
    string Document,
    string Engine,
    bool Success,
    int Pages,
    TimeSpan Duration,
    int Words,
    int Tables,
    TextQuality? Quality,
    bool NativeTextLayer,
    string? Error);

/// <summary>Raport porównawczy: report.md (do czytania) i report.csv (do arkusza).</summary>
public static class ReportWriter
{
    private static readonly CultureInfo Pl = CultureInfo.GetCultureInfo("pl-PL");

    public static async Task WriteAsync(string directory, DateTimeOffset started, IReadOnlyList<EngineRun> engines, IReadOnlyList<DocumentRun> runs, CancellationToken ct)
    {
        await File.WriteAllTextAsync(Path.Combine(directory, "report.md"), BuildMarkdown(started, engines, runs), Encoding.UTF8, ct);
        await File.WriteAllTextAsync(Path.Combine(directory, "report.csv"), BuildCsv(runs), new UTF8Encoding(true), ct);
    }

    public static string BuildMarkdown(DateTimeOffset started, IReadOnlyList<EngineRun> engines, IReadOnlyList<DocumentRun> runs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Porównanie silników OCR");
        sb.AppendLine();
        sb.AppendLine(Pl, $"- Start: {started:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine(Pl, $"- Procesory logiczne (runner): {Environment.ProcessorCount}");
        sb.AppendLine(Pl, $"- Dokumenty: {runs.Select(r => r.Document).Distinct().Count()}, w tym z wzorcem (.gt.txt): {runs.Where(r => r.Quality is not null).Select(r => r.Document).Distinct().Count()}");
        sb.AppendLine();

        sb.AppendLine("## Silniki");
        sb.AppendLine();
        sb.AppendLine("| Silnik | Dostępny | Wersja / błąd |");
        sb.AppendLine("|---|---|---|");
        foreach (var e in engines)
        {
            sb.AppendLine(Pl, $"| {e.Engine} | {(e.Status.IsAvailable ? "tak" : "**nie**")} | {Cell(e.Status.IsAvailable ? e.Status.Version : e.Status.Error ?? string.Empty)} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Podsumowanie");
        sb.AppendLine();
        sb.AppendLine("CER/WER: im mniej, tym lepiej (0 % = idealnie). Pokrycie słów: odsetek słów wzorca obecnych w wyniku niezależnie od kolejności (100 % = wszystkie).");
        sb.AppendLine("Metryki jakości są średnią z dokumentów, które mają wzorzec `.gt.txt`.");
        sb.AppendLine();
        sb.AppendLine("| Silnik | OK / błędy | Strony | Czas łącznie | s / stronę | CER | WER | Pokrycie słów | Tabele | Warstwa tekstowa |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
        foreach (var group in runs.GroupBy(r => r.Engine))
        {
            var ok = group.Where(r => r.Success).ToList();
            var pages = ok.Sum(r => r.Pages);
            var total = TimeSpan.FromTicks(ok.Sum(r => r.Duration.Ticks));
            var withQuality = ok.Where(r => r.Quality is not null).Select(r => r.Quality!).ToList();
            sb.AppendLine(Pl,
                $"| {group.Key} | {ok.Count} / {group.Count() - ok.Count} | {pages} | {Duration(total)} | {(pages > 0 ? (total.TotalSeconds / pages).ToString("0.0", Pl) : "–")} " +
                $"| {Avg(withQuality, q => q.Cer)} | {Avg(withQuality, q => q.Wer)} | {Avg(withQuality, q => q.WordRecall)} | {ok.Sum(r => r.Tables)} " +
                $"| {(ok.Any(r => r.NativeTextLayer) ? "natywna (silnik)" : "generowana (PdfPig)")} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Wyniki per dokument");
        foreach (var document in runs.GroupBy(r => r.Document))
        {
            sb.AppendLine();
            sb.AppendLine(Pl, $"### {document.Key}");
            sb.AppendLine();
            sb.AppendLine("| Silnik | Status | Strony | Czas | s / stronę | Słowa | Tabele | CER | WER | Pokrycie słów |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|");
            foreach (var r in document.OrderBy(r => r.Engine, StringComparer.Ordinal))
            {
                if (!r.Success)
                {
                    sb.AppendLine(Pl, $"| {r.Engine} | **błąd** | | {Duration(r.Duration)} | | | | | | {Cell(r.Error ?? string.Empty, 200)} |");
                    continue;
                }

                sb.AppendLine(Pl,
                    $"| {r.Engine} | OK | {r.Pages} | {Duration(r.Duration)} | {(r.Pages > 0 ? (r.Duration.TotalSeconds / r.Pages).ToString("0.0", Pl) : "–")} | {r.Words} | {r.Tables} " +
                    $"| {Pct(r.Quality?.Cer)} | {Pct(r.Quality?.Wer)} | {Pct(r.Quality?.WordRecall)} |");
            }
        }

        return sb.ToString();
    }

    public static string BuildCsv(IReadOnlyList<DocumentRun> runs)
    {
        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine("document;engine;success;pages;duration_s;s_per_page;words;tables;cer;wer;word_recall;native_text_layer;error");
        foreach (var r in runs)
        {
            sb.AppendLine(string.Join(";",
                r.Document, r.Engine, r.Success ? "1" : "0", r.Pages.ToString(inv),
                r.Duration.TotalSeconds.ToString("0.000", inv),
                r.Pages > 0 ? (r.Duration.TotalSeconds / r.Pages).ToString("0.000", inv) : string.Empty,
                r.Words.ToString(inv), r.Tables.ToString(inv),
                r.Quality?.Cer.ToString("0.0000", inv) ?? string.Empty,
                r.Quality?.Wer.ToString("0.0000", inv) ?? string.Empty,
                r.Quality?.WordRecall.ToString("0.0000", inv) ?? string.Empty,
                r.NativeTextLayer ? "1" : "0",
                (r.Error ?? string.Empty).ReplaceLineEndings(" ").Replace(';', ',')));
        }

        return sb.ToString();
    }

    private static string Avg(IReadOnlyList<TextQuality> values, Func<TextQuality, double> selector) =>
        values.Count == 0 ? "–" : Pct(values.Average(selector));

    private static string Pct(double? value) => value is null ? "–" : (value.Value * 100).ToString("0.00", Pl) + " %";

    private static string Duration(TimeSpan d) =>
        d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} min {d.Seconds} s" : d.TotalSeconds.ToString("0.0", Pl) + " s";

    private static string Cell(string value, int max = 300)
    {
        var single = value.ReplaceLineEndings(" ").Replace("|", "\\|");
        return single.Length <= max ? single : single[..max] + "…";
    }
}
