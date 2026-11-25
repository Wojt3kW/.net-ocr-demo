namespace OcrDemo.Core.Pdf;

/// <summary>
/// Czcionka TrueType dla niewidocznej warstwy tekstowej – musi zawierać polskie znaki.
/// W kontenerze: DejaVu Sans (pakiet fonts-dejavu-core), lokalnie na Windows: Arial.
/// </summary>
public static class PdfFonts
{
    /// <summary>Kandydaci sprawdzani po kolei, gdy nie podano ścieżki jawnie.</summary>
    public static readonly string[] DefaultCandidates =
    [
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
        "/usr/share/fonts/dejavu/DejaVuSans.ttf",
        @"C:\Windows\Fonts\arial.ttf",
        "/System/Library/Fonts/Supplemental/Arial.ttf",
    ];

    public static byte[] Load(string? fontPath = null)
    {
        var path = !string.IsNullOrWhiteSpace(fontPath)
            ? fontPath
            : DefaultCandidates.FirstOrDefault(File.Exists);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Nie znaleziono czcionki TTF dla warstwy tekstowej. Ustaw Pdf:FontPath. Sprawdzone: {string.Join(", ", DefaultCandidates)}",
                fontPath);
        }

        return File.ReadAllBytes(path);
    }
}
