using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using OcrDemo.Core;
using OcrDemo.Core.Model;
using OcrDemo.Core.Pdf;
using OcrDemo.Engines.Sidecar;

namespace OcrDemo.Engines;

/// <summary>
/// OCRmyPDF + Tesseract. Silnik zwraca gotowy PDF z warstwą tekstową (natywny wynik) i tekst stron
/// w kolejności czytania Tesseracta; słowa z położeniem odczytujemy z warstwy PDF (PdfPig). Brak tabel.
/// </summary>
public sealed class OcrMyPdfEngine(HttpClient http) : IOcrEngine
{
    public string Name => "ocrmypdf";

    public Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken) =>
        SidecarClient.GetStatusAsync(http, cancellationToken);

    public async Task<OcrDocumentResult> RecognizeAsync(OcrInput input, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        // Zawsze PDF: obrazy są wcześniej zamienione na PDF z poprawnym DPI (OCRmyPDF wymaga DPI dla obrazów).
        using var response = await SidecarClient.PostDocumentAsync(http, input.BasePdf, Path.ChangeExtension(input.FileName, ".pdf"), cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<OcrMyPdfResponse>(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken)
                      ?? throw new OcrEngineException("Pusta odpowiedź kontenera.");
        stopwatch.Stop();

        var pdf = Convert.FromBase64String(payload.Pdf);
        var pages = PdfTextLayerReader.Read(pdf);
        if (payload.Pages.Count == pages.Count)
        {
            // Tekst w kolejności Tesseracta, geometria z warstwy PDF.
            pages = pages.Select((p, i) => p with { Text = payload.Pages[i] }).ToList();
        }

        var duration = SidecarClient.ProcessingTime(response, stopwatch.Elapsed);
        return new OcrDocumentResult(Name, payload.Version, pages, duration, NativeSearchablePdf: pdf);
    }

    private sealed record OcrMyPdfResponse(
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("pdf")] string Pdf,
        [property: JsonPropertyName("pages")] List<string> Pages);
}
