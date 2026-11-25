using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OcrDemo.Core;
using OcrDemo.Core.Input;
using OcrDemo.Core.Metrics;
using OcrDemo.Core.Model;
using OcrDemo.Core.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Writer;

namespace OcrDemo.Runner;

/// <summary>
/// Przetwarza wszystkie dokumenty z katalogu wejściowego każdym dostępnym silnikiem (sekwencyjnie,
/// aby pomiary czasu były porównywalne) i zapisuje wyniki oraz raport.
/// </summary>
/// <remarks>
/// Docelowo w serwisie: ta sama sekwencja (InputDocumentLoader → IOcrEngine → SearchablePdfWriter)
/// wywoływana dla pojedynczej wiadomości z RabbitMQ zamiast pętli po katalogu.
/// </remarks>
public sealed class BenchmarkRunner(
    IEnumerable<IOcrEngine> allEngines,
    IOptions<RunnerOptions> options,
    ILogger<BenchmarkRunner> logger)
{
    private readonly RunnerOptions _options = options.Value;

    public async Task<int> RunAsync(CancellationToken ct)
    {
        var started = DateTimeOffset.Now;
        var inputDir = Path.GetFullPath(_options.InputDirectory);
        if (!Directory.Exists(inputDir))
        {
            logger.LogError("Brak katalogu wejściowego {Dir}", inputDir);
            return 2;
        }

        var files = Directory.EnumerateFiles(inputDir)
            .Where(InputDocumentLoader.IsSupported)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count == 0)
        {
            logger.LogWarning("Brak dokumentów w {Dir}. Obsługiwane rozszerzenia: {Ext}", inputDir, string.Join(", ", InputDocumentLoader.SupportedExtensions));
            return 0;
        }

        var engines = SelectEngines();
        var engineRuns = new List<EngineRun>();
        var available = new List<IOcrEngine>();
        foreach (var engine in engines)
        {
            var status = await engine.GetStatusAsync(ct);
            engineRuns.Add(new EngineRun(engine.Name, status));
            if (status.IsAvailable)
            {
                logger.LogInformation("Silnik {Engine}: {Version}", engine.Name, status.Version);
                available.Add(engine);
            }
            else
            {
                logger.LogWarning("Silnik {Engine} niedostępny: {Error}", engine.Name, status.Error);
            }
        }

        if (available.Count == 0)
        {
            logger.LogError("Żaden silnik nie jest dostępny – uruchom kontenery (docker compose up -d).");
            return 3;
        }

        var outputDir = Path.Combine(Path.GetFullPath(_options.OutputDirectory), started.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outputDir);
        logger.LogInformation("Dokumentów: {Count}, wyniki: {Dir}", files.Count, outputDir);

        var loader = new InputDocumentLoader(_options.FallbackImageDpi);
        var pdfWriter = new SearchablePdfWriter(_options.FontPath);

        if (_options.Warmup)
        {
            await WarmupAsync(available, await loader.LoadAsync(files[0], ct), ct);
        }

        var runs = new List<DocumentRun>();
        foreach (var file in files)
        {
            var documentName = Path.GetFileName(file);
            var documentDir = Path.Combine(outputDir, documentName);
            OcrInput input;
            try
            {
                input = await loader.LoadAsync(file, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Nie udało się wczytać {File}", documentName);
                runs.AddRange(available.Select(e => new DocumentRun(documentName, e.Name, false, 0, TimeSpan.Zero, 0, 0, null, false, $"Wczytywanie: {ex.Message}")));
                continue;
            }

            var groundTruth = LoadGroundTruth(file);
            logger.LogInformation("── {File}: {Pages} str.{Gt}", documentName, input.PageCount, groundTruth is null ? string.Empty : ", jest wzorzec");

            foreach (var engine in available)
            {
                runs.Add(await RunEngineAsync(engine, input, documentDir, groundTruth, pdfWriter, ct));
            }

            // Raport aktualizowany po każdym dokumencie – częściowe wyniki przy długich przebiegach.
            await ReportWriter.WriteAsync(outputDir, started, engineRuns, runs, ct);
        }

        logger.LogInformation("Gotowe. Raport: {Report}", Path.Combine(outputDir, "report.md"));
        return 0;
    }

    private async Task<DocumentRun> RunEngineAsync(IOcrEngine engine, OcrInput input, string documentDir, string? groundTruth, SearchablePdfWriter pdfWriter, CancellationToken ct)
    {
        var documentName = input.FileName;
        try
        {
            var result = await engine.RecognizeAsync(input, ct);
            var pdf = result.NativeSearchablePdf ?? pdfWriter.Write(input.BasePdf, result);
            await ResultWriter.WriteAsync(documentDir, result, pdf, _options.SaveRawOutput, ct);

            var quality = groundTruth is null ? null : TextMetrics.Compare(groundTruth, result.Text);
            logger.LogInformation("   {Engine,-18} {Seconds,7:0.0} s  słowa: {Words,6}  tabele: {Tables,3}{Quality}",
                engine.Name, result.Duration.TotalSeconds, result.WordCount, result.Tables.Count(),
                quality is null ? string.Empty : $"  CER: {quality.Cer:P2}  WER: {quality.Wer:P2}");

            return new DocumentRun(documentName, engine.Name, true, result.Pages.Count, result.Duration, result.WordCount,
                result.Tables.Count(), quality, result.NativeSearchablePdf is not null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogError("   {Engine,-18} BŁĄD: {Message}", engine.Name, ex.Message);
            await ResultWriter.WriteErrorAsync(documentDir, engine.Name, ex, ct);
            return new DocumentRun(documentName, engine.Name, false, 0, TimeSpan.Zero, 0, 0, null, false, ex.Message);
        }
    }

    private async Task WarmupAsync(IReadOnlyList<IOcrEngine> engines, OcrInput firstDocument, CancellationToken ct)
    {
        var firstPage = FirstPageOnly(firstDocument);
        foreach (var engine in engines)
        {
            logger.LogInformation("Rozgrzewka {Engine} (1. strona {File}, bez pomiaru)…", engine.Name, firstDocument.FileName);
            try
            {
                await engine.RecognizeAsync(firstPage, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning("Rozgrzewka {Engine} nieudana: {Message}", engine.Name, ex.Message);
            }
        }
    }

    /// <summary>Jednostronicowy PDF z pierwszą stroną dokumentu (do rozgrzewki).</summary>
    private static OcrInput FirstPageOnly(OcrInput input)
    {
        if (input.PageCount <= 1)
        {
            return input;
        }

        using var source = PdfDocument.Open(input.BasePdf);
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(source, 1);
        var pdf = builder.Build();
        return new OcrInput(Path.ChangeExtension(input.FilePath, ".warmup.pdf"), InputKind.Pdf, pdf, pdf, 1);
    }

    private IReadOnlyList<IOcrEngine> SelectEngines()
    {
        var engines = allEngines.ToList();
        if (string.IsNullOrWhiteSpace(_options.EngineFilter))
        {
            return engines;
        }

        var wanted = _options.EngineFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = wanted.Except(engines.Select(e => e.Name), StringComparer.OrdinalIgnoreCase).ToList();
        if (unknown.Count > 0)
        {
            logger.LogWarning("Nieznane lub wyłączone silniki w filtrze: {Unknown}. Dostępne: {All}",
                string.Join(", ", unknown), string.Join(", ", engines.Select(e => e.Name)));
        }

        return engines.Where(e => wanted.Contains(e.Name)).ToList();
    }

    /// <summary>Wzorzec dla „faktura.pdf” to „faktura.gt.txt” w tym samym katalogu.</summary>
    private static string? LoadGroundTruth(string documentPath)
    {
        var path = Path.Combine(Path.GetDirectoryName(documentPath)!, Path.GetFileNameWithoutExtension(documentPath) + ".gt.txt");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }
}
