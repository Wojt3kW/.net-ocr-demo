using OcrDemo.Core.Model;

namespace OcrDemo.Core;

/// <summary>Silnik OCR. Implementacje są bezstanowe i mogą być wywoływane wielokrotnie.</summary>
public interface IOcrEngine
{
    /// <summary>Unikalna nazwa (używana w nazwach plików wynikowych i w raporcie).</summary>
    string Name { get; }

    /// <summary>Sprawdza dostępność silnika i zwraca jego wersję.</summary>
    Task<EngineStatus> GetStatusAsync(CancellationToken cancellationToken);

    Task<OcrDocumentResult> RecognizeAsync(OcrInput input, CancellationToken cancellationToken);
}

public sealed record EngineStatus(bool IsAvailable, string Version, string? Error = null);
