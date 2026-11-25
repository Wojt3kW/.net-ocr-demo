using System.Net.Http.Headers;
using System.Net.Http.Json;
using OcrDemo.Core;

namespace OcrDemo.Engines.Sidecar;

/// <summary>Wspólna obsługa kontenerów z <c>sidecars/common/ocr_http.py</c> (GET /health, POST /ocr).</summary>
internal static class SidecarClient
{
    public static async Task<EngineStatus> GetStatusAsync(HttpClient http, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var health = await http.GetFromJsonAsync<SidecarHealth>("health", timeout.Token);
            return health is { Status: "ok" }
                ? new EngineStatus(true, health.Version)
                : new EngineStatus(false, string.Empty, $"Nieoczekiwana odpowiedź /health: {health?.Status}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return new EngineStatus(false, string.Empty, $"{http.BaseAddress}: {ex.Message}");
        }
    }

    public static async Task<HttpResponseMessage> PostDocumentAsync(HttpClient http, byte[] content, string fileName, CancellationToken cancellationToken)
    {
        using var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        var response = await http.PostAsync($"ocr?filename={Uri.EscapeDataString(fileName)}", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new OcrEngineException($"HTTP {(int)response.StatusCode}: {Truncate(error, 4000)}");
        }

        return response;
    }

    /// <summary>
    /// Czas przetwarzania zmierzony w kontenerze (bez przesyłu HTTP) – nagłówek X-Processing-Time-Ms.
    /// Wszystkie silniki raportują czas po stronie serwera, aby pomiary były porównywalne.
    /// </summary>
    public static TimeSpan ProcessingTime(HttpResponseMessage response, TimeSpan fallback) =>
        response.Headers.TryGetValues("X-Processing-Time-Ms", out var values)
        && long.TryParse(values.FirstOrDefault(), out var ms)
            ? TimeSpan.FromMilliseconds(ms)
            : fallback;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

public sealed class OcrEngineException(string message) : Exception(message);
