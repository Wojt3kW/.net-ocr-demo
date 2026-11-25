using OcrDemo.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Operations.TextState;
using UglyToad.PdfPig.Writer;

namespace OcrDemo.Core.Pdf;

/// <summary>
/// Nakłada niewidoczną warstwę tekstową (tryb renderowania tekstu 3 = "neither fill nor stroke")
/// na istniejący PDF – tak samo jak robią to Tesseract/OCRmyPDF/ABBYY. Tekst jest zaznaczalny i wyszukiwalny.
/// Strony są kopiowane bez zmian, tekst dopisywany jest w nowym strumieniu treści.
/// </summary>
/// <remarks>Ograniczenie: strony z atrybutem /Rotate ≠ 0 nie są obsługiwane (tekst trafi w złe miejsce).</remarks>
public sealed class SearchablePdfWriter(byte[] fontBytes)
{
    public SearchablePdfWriter(string? fontPath = null)
        : this(PdfFonts.Load(fontPath))
    {
    }

    public byte[] Write(byte[] basePdf, OcrDocumentResult result)
    {
        using var source = PdfDocument.Open(basePdf);
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddTrueTypeFont(fontBytes);
        var pagesByNumber = result.Pages.ToDictionary(p => p.Number);

        for (var number = 1; number <= source.NumberOfPages; number++)
        {
            var pageBuilder = builder.AddPage(source, number);
            if (!pagesByNumber.TryGetValue(number, out var ocrPage) || ocrPage.Lines.Count == 0)
            {
                continue;
            }

            var bounds = source.GetPage(number).CropBox.Bounds;
            pageBuilder.NewContentStreamAfter();
            pageBuilder.SetTextRenderingMode(TextRenderingMode.Neither);

            // Cała linia jako jeden obiekt tekstowy (ze spacjami), rozciągnięta na szerokość ramki linii.
            // Rysowanie słowo po słowie daje dokładniejsze ramki, ale część czytników PDF (pdfium/Chrome)
            // skleja wtedy słowa przy kopiowaniu tekstu. Kolejność linii = kolejność czytania silnika.
            foreach (var line in ocrPage.Lines)
            {
                DrawLine(pageBuilder, font, line.Text, line.Box, bounds);
            }

            pageBuilder.CurrentStream.Operations.Add(new SetHorizontalScaling(100));
        }

        return builder.Build();
    }

    private static void DrawLine(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, NormBox box, PdfRectangle bounds)
    {
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length == 0 || box.IsEmpty)
        {
            return;
        }

        // Współrzędne znormalizowane (od lewego górnego rogu) -> punkty PDF (od lewego dolnego rogu).
        var left = bounds.Left + box.X0 * bounds.Width;
        var width = box.Width * bounds.Width;
        var height = box.Height * bounds.Height;
        var bottom = bounds.Top - box.Y1 * bounds.Height;

        // Wysokość czcionki ~ wysokość ramki; linia bazowa nieco nad dolną krawędzią (miejsce na ogonki: ą, ę, g, p).
        var fontSize = Math.Max(1d, height * 0.85);
        var origin = new PdfPoint(left, bottom + height * 0.2);

        var measured = page.MeasureText(text, fontSize, origin, font);
        if (measured.Count == 0)
        {
            return;
        }

        var naturalWidth = measured[^1].EndBaseLine.X - measured[0].StartBaseLine.X;
        if (naturalWidth <= 0)
        {
            return;
        }

        // Skalowanie poziome (operator Tz), aby tekst pokrywał ramkę linii na obrazie.
        // Rozciąganie ograniczone do 130 %: luźne ramki (np. całe komórki tabeli) nie mogą stykać się
        // z sąsiednim tekstem – inaczej czytniki PDF sklejają wartości sąsiednich komórek.
        // Margines 4 % z prawej – odstęp od tekstu w kolejnej ramce na tej samej wysokości.
        var scale = Math.Clamp(width * 0.96 / naturalWidth * 100, 1, 130);
        page.CurrentStream.Operations.Add(new SetHorizontalScaling(scale));
        page.AddText(text, fontSize, origin, font);
    }
}
