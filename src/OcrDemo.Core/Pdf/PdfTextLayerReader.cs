using System.Text;
using OcrDemo.Core.Model;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace OcrDemo.Core.Pdf;

/// <summary>
/// Odczytuje warstwę tekstową PDF (np. wygenerowaną przez OCRmyPDF) do wspólnego modelu:
/// słowa z położeniem, linie i bloki w kolejności czytania (analiza układu PdfPig: Docstrum + reading order).
/// </summary>
public static class PdfTextLayerReader
{
    public static IReadOnlyList<OcrPage> Read(byte[] pdf)
    {
        using var document = PdfDocument.Open(pdf);
        var pages = new List<OcrPage>(document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            pages.Add(ReadPage(page));
        }

        return pages;
    }

    private static OcrPage ReadPage(Page page)
    {
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();
        if (words.Count == 0)
        {
            return new OcrPage(page.Number, string.Empty, [], []);
        }

        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks);

        var lines = new List<OcrLine>();
        var text = new StringBuilder();
        foreach (var block in ordered)
        {
            foreach (var textLine in block.TextLines)
            {
                var lineWords = textLine.Words
                    .Select(w => new OcrWord(w.Text, ToNorm(w.BoundingBox, page), null))
                    .ToList();
                var lineText = string.Join(" ", lineWords.Select(w => w.Text));
                lines.Add(new OcrLine(lineText, ToNorm(textLine.BoundingBox, page), null, lineWords));
                text.AppendLine(lineText);
            }
        }

        return new OcrPage(page.Number, text.ToString().TrimEnd(), lines, []);
    }

    /// <summary>PDF: początek w lewym dolnym rogu, jednostki pt → znormalizowane 0..1 od lewego górnego rogu.</summary>
    private static NormBox ToNorm(UglyToad.PdfPig.Core.PdfRectangle r, Page page) =>
        NormBox.FromPixels(r.Left, page.Height - r.Top, r.Right, page.Height - r.Bottom, page.Width, page.Height);
}
