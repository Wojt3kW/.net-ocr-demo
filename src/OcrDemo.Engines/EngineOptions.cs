namespace OcrDemo.Engines;

public class SidecarEngineOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Adres kontenera, np. http://localhost:8101 (lokalnie) lub http://ocrmypdf:8000 (docker compose).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Limit czasu na jeden dokument (CPU bywa wolne – np. PP-StructureV3 ~50 s/stronę).</summary>
    public int TimeoutSeconds { get; set; } = 3600;
}

public sealed class OcrMyPdfOptions : SidecarEngineOptions;

public sealed class PaddleOcrOptions : SidecarEngineOptions;

public sealed class DoctrOptions : SidecarEngineOptions;

public sealed class DoclingOptions : SidecarEngineOptions
{
    /// <summary>
    /// Silniki OCR wewnątrz Docling (parametr ocr_preset) – każdy jest osobną pozycją w porównaniu.
    /// Dostępne w obrazie docling-serve-cpu: rapidocr, easyocr, tesserocr, tesseract (tylko eng).
    /// </summary>
    public List<string> OcrPresets { get; set; } = ["rapidocr", "easyocr"];

    /// <summary>Języki BCP-47 przekazywane jako ocr_lang.</summary>
    public List<string> OcrLanguages { get; set; } = ["pl", "en"];

    /// <summary>accurate | fast (TableFormer).</summary>
    public string TableMode { get; set; } = "accurate";
}

public static class EngineSections
{
    public const string Root = "Engines";
    public const string OcrMyPdf = $"{Root}:OcrMyPdf";
    public const string PaddleOcr = $"{Root}:PaddleOcr";
    public const string Doctr = $"{Root}:Doctr";
    public const string Docling = $"{Root}:Docling";
}
