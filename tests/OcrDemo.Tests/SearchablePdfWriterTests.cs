using ImageMagick;
using OcrDemo.Core.Input;
using OcrDemo.Core.Model;
using OcrDemo.Core.Pdf;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;

namespace OcrDemo.Tests;

public class SearchablePdfWriterTests
{
    private static byte[] CreatePng(int width, int height, double dpi)
    {
        using var image = new MagickImage(MagickColors.White, (uint)width, (uint)height);
        image.Density = new Density(dpi, dpi, DensityUnit.PixelsPerInch);
        image.Format = MagickFormat.Png;
        return image.ToByteArray();
    }

    [Fact]
    public void ImagesToPdf_UsesImageDpiForPageSize()
    {
        var png = CreatePng(2480, 3508, 300); // A4 @ 300 DPI

        var (pdf, pages) = new InputDocumentLoader().ImagesToPdf(png);

        Assert.Equal(1, pages);
        using var doc = PdfDocument.Open(pdf);
        var page = doc.GetPage(1);
        Assert.Equal(595.2, page.Width, 1);
        Assert.Equal(841.9, page.Height, 1);
    }

    [Fact]
    public void Write_AddsInvisibleSelectablePolishText()
    {
        var (basePdf, _) = new InputDocumentLoader().ImagesToPdf(CreatePng(1000, 1000, 100));
        var words = new[]
        {
            new OcrWord("Zażółć", new NormBox(0.10, 0.10, 0.30, 0.15), 0.9),
            new OcrWord("gęślą", new NormBox(0.32, 0.10, 0.50, 0.15), 0.9),
        };
        var result = new OcrDocumentResult("test", "1.0",
            [new OcrPage(1, "Zażółć gęślą", [new OcrLine("Zażółć gęślą", new NormBox(0.10, 0.10, 0.50, 0.15), 0.9, words), new OcrLine("Łączniki", new NormBox(0.1, 0.5, 0.4, 0.55), null, [])], [])],
            TimeSpan.Zero);

        var output = new SearchablePdfWriter().Write(basePdf, result);

        using var doc = PdfDocument.Open(output);
        var page = doc.GetPage(1);
        var pdfWords = page.GetWords().Select(w => w.Text).ToList();
        Assert.Contains("Zażółć", pdfWords);
        Assert.Contains("gęślą", pdfWords);
        Assert.Contains("Łączniki", pdfWords);
        Assert.All(page.Letters.Where(l => !string.IsNullOrWhiteSpace(l.Value)),
            l => Assert.Equal(TextRenderingMode.Neither, l.RenderingMode));

        // Położenie: linia rysowana w całości w ramce linii (PDF: Y od dołu).
        var first = page.GetWords().First(w => w.Text == "Zażółć");
        var last = page.GetWords().First(w => w.Text == "gęślą");
        Assert.InRange(first.BoundingBox.Left / page.Width, 0.08, 0.12);
        Assert.InRange(last.BoundingBox.Right / page.Width, 0.40, 0.51);
        Assert.InRange(1 - first.BoundingBox.Top / page.Height, 0.08, 0.13);
    }

    [Fact]
    public void PdfTextLayerReader_ReadsBackWordsWithNormalizedBoxes()
    {
        var (basePdf, _) = new InputDocumentLoader().ImagesToPdf(CreatePng(1000, 1000, 100));
        var line = new OcrLine("Faktura VAT", new NormBox(0.1, 0.2, 0.4, 0.25), null,
            [new OcrWord("Faktura", new NormBox(0.1, 0.2, 0.28, 0.25), null), new OcrWord("VAT", new NormBox(0.3, 0.2, 0.4, 0.25), null)]);
        var pdf = new SearchablePdfWriter().Write(basePdf,
            new OcrDocumentResult("test", "1.0", [new OcrPage(1, "Faktura VAT", [line], [])], TimeSpan.Zero));

        var pages = PdfTextLayerReader.Read(pdf);

        var page = Assert.Single(pages);
        Assert.Equal("Faktura VAT", page.Text);
        var readLine = Assert.Single(page.Lines);
        Assert.InRange(readLine.Box.X0, 0.08, 0.12);
        Assert.InRange(readLine.Box.Y0, 0.17, 0.23);
    }
}
