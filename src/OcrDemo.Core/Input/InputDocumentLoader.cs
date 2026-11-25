using ImageMagick;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace OcrDemo.Core.Input;

/// <summary>Wczytuje plik wejściowy i przygotowuje <see cref="OcrInput"/> (w tym bazowy PDF dla obrazów).</summary>
public sealed class InputDocumentLoader(double fallbackDpi = 300)
{
    public static readonly IReadOnlySet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".png", ".jpg", ".jpeg", ".tif", ".tiff", ".bmp",
    };

    public static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    public async Task<OcrInput> LoadAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        if (string.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            using var pdf = PdfDocument.Open(bytes);
            return new OcrInput(path, InputKind.Pdf, bytes, bytes, pdf.NumberOfPages);
        }

        var (basePdf, pages) = ImagesToPdf(bytes);
        return new OcrInput(path, InputKind.Image, bytes, basePdf, pages);
    }

    /// <summary>
    /// Każda ramka obrazu (np. strony wielostronicowego TIFF) staje się stroną PDF
    /// o rozmiarze wynikającym z DPI zapisanego w pliku (lub <c>fallbackDpi</c>, gdy go brak).
    /// </summary>
    public (byte[] Pdf, int PageCount) ImagesToPdf(byte[] imageBytes)
    {
        using var frames = new MagickImageCollection(imageBytes);
        using var builder = new PdfDocumentBuilder();
        foreach (var frame in frames)
        {
            var (dpiX, dpiY) = GetDpi(frame);
            // Bez AutoOrient(): silniki dostają oryginalny plik bez obrotu EXIF,
            // więc strona PDF musi mieć tę samą geometrię co obraz widziany przez silnik.
            var width = frame.Width * 72d / dpiX;
            var height = frame.Height * 72d / dpiY;
            var page = builder.AddPage(width, height);
            var rect = new PdfRectangle(0, 0, width, height);

            if (frame.Format is MagickFormat.Jpeg or MagickFormat.Jpg)
            {
                page.AddJpeg(frame.ToByteArray(MagickFormat.Jpeg), rect);
                continue;
            }

            // PNG 8-bit (szarość lub RGB), bez kanału alfa – format obsługiwany przez zapis PdfPig.
            var gray = frame.DetermineColorType() is ColorType.Bilevel or ColorType.Grayscale or ColorType.GrayscaleAlpha;
            frame.BackgroundColor = MagickColors.White;
            frame.Alpha(AlphaAction.Remove);
            frame.Depth = 8;
            frame.ColorType = gray ? ColorType.Grayscale : ColorType.TrueColor;
            frame.Settings.SetDefine(MagickFormat.Png, "color-type", gray ? "0" : "2");
            frame.Settings.SetDefine(MagickFormat.Png, "bit-depth", "8");
            page.AddPng(frame.ToByteArray(MagickFormat.Png), rect);
        }

        return (builder.Build(), frames.Count);
    }

    private (double X, double Y) GetDpi(IMagickImage image)
    {
        var density = image.Density;
        var x = density.X;
        var y = density.Y;
        if (density.Units == DensityUnit.PixelsPerCentimeter)
        {
            x *= 2.54;
            y *= 2.54;
        }

        // Brak DPI w pliku – używamy wartości zastępczej (wpływa tylko na fizyczny rozmiar strony PDF).
        if (x <= 1 || y <= 1 || density.Units == DensityUnit.Undefined)
        {
            return (fallbackDpi, fallbackDpi);
        }

        return (x, y);
    }
}
