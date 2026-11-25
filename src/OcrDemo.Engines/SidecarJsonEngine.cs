using System.Diagnostics;
using System.Text.Json;
using OcrDemo.Core;
using OcrDemo.Core.Model;
using OcrDemo.Engines.Sidecar;

namespace OcrDemo.Engines;

/// <summary>
/// Silnik w kontenerze Python zwracający wspólny kontrakt JSON (PaddleOCR PP-StructureV3, docTR).
/// Kontener dostaje oryginalny plik (PDF rasteryzuje sam w 300 DPI, obrazy w natywnej rozdzielczości).
/// </summary>
public sealed class SidecarJsonEngine(string name, HttpClient http) : IOcrEngine
{
    public string Name => name;

    public Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        SidecarClient.GetStatusAsync(http, cancellationToken);

    public async Task<OcrDocumentResult> RecognizeAsync(OcrInput input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var response = await SidecarClient.PostDocumentAsync(http, input.OriginalBytes, input.FileName, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        var payload = JsonSerializer.Deserialize<SidecarResponse>(json)
                      ?? throw new OcrEngineException("Pusta odpowiedź kontenera.");
        var pages = payload.Pages.Select(SidecarMapping.ToPage).ToList();
        var duration = SidecarClient.ProcessingTime(response, stopwatch.Elapsed);
        return new OcrDocumentResult(Name, payload.Version, pages, duration, RawJson: json);
    }
}
