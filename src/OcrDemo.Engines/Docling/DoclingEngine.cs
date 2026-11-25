using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using OcrDemo.Core;
using OcrDemo.Core.Model;
using OcrDemo.Engines.Sidecar;

namespace OcrDemo.Engines.Docling;

/// <summary>
/// Docling Serve (oficjalny obraz docling-serve-cpu): układ strony (Heron) + tabele (TableFormer)
/// + wybrany silnik OCR (<paramref name="ocrPreset"/>). Każdy preset to osobna pozycja w porównaniu.
/// </summary>
public sealed class DoclingEngine(HttpClient http, DoclingOptions options, string ocrPreset) : IOcrEngine
{
    private string? _version;

    public string Name => $"docling-{ocrPreset}";

    public async Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var versions = await http.GetFromJsonAsync<Dictionary<string, string>>("version", timeout.Token) ?? [];
            var version = $"docling-serve {versions.GetValueOrDefault("docling-serve")} (docling {versions.GetValueOrDefault("docling")}), OCR: {ocrPreset}";
            _version = version;
            return new EngineStatus(true, version);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new EngineStatus(false, string.Empty, $"{http.BaseAddress}: {ex.Message}");
        }
    }

    public async Task<OcrDocumentResult> RecognizeAsync(OcrInput input, CancellationToken cancellationToken)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(input.OriginalBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", input.FileName);
        form.Add(new StringContent("json"), "to_formats");
        form.Add(new StringContent("true"), "do_ocr");
        form.Add(new StringContent("true"), "force_ocr");
        form.Add(new StringContent(ocrPreset), "ocr_preset");
        foreach (var language in options.OcrLanguages)
        {
            form.Add(new StringContent(language), "ocr_lang");
        }

        form.Add(new StringContent("true"), "do_table_structure");
        form.Add(new StringContent(options.TableMode), "table_mode");
        form.Add(new StringContent("placeholder"), "image_export_mode");
        form.Add(new StringContent("false"), "include_images");
        form.Add(new StringContent("true"), "abort_on_error");
        form.Add(new StringContent(options.TimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)), "document_timeout");

        var stopwatch = Stopwatch.StartNew();
        using var response = await http.PostAsync("v1/convert/file", form, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
        {
            throw new OcrEngineException($"HTTP {(int)response.StatusCode}: {json[..Math.Min(json.Length, 4000)]}");
        }

        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;
        var status = root.GetProperty("status").GetString();
        if (status is not ("success" or "partial_success"))
        {
            throw new OcrEngineException($"Docling status={status}, errors={root.GetProperty("errors")}");
        }

        var document = root.GetProperty("document").GetProperty("json_content");
        var pages = DoclingDocumentMapper.Map(document);
        var version = _version ?? (await GetStatusAsync(cancellationToken)).Version;

        // Czas konwersji raportowany przez Docling. Czas zegarowy synchronicznego endpointu jest
        // zaokrąglany w górę do interwału odpytywania kolejki (sync_poll_interval, domyślnie 2 s).
        var duration = root.TryGetProperty("processing_time", out var pt) && pt.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromSeconds(pt.GetDouble())
            : stopwatch.Elapsed;
        return new OcrDocumentResult(Name, version, pages, duration, RawJson: json);
    }
}
