namespace OcrDemo.Core.Model;

/// <summary>
/// Prostokąt we współrzędnych znormalizowanych 0..1 względem strony,
/// początek układu w lewym górnym rogu (X w prawo, Y w dół).
/// </summary>
public readonly record struct NormBox(double X0, double Y0, double X1, double Y1)
{
    public double Width => X1 - X0;
    public double Height => Y1 - Y0;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static NormBox FromPixels(double x0, double y0, double x1, double y1, double pageWidth, double pageHeight) =>
        new(Clamp(x0 / pageWidth), Clamp(y0 / pageHeight), Clamp(x1 / pageWidth), Clamp(y1 / pageHeight));

    private static double Clamp(double v) => Math.Clamp(v, 0d, 1d);
}

public sealed record OcrWord(string Text, NormBox Box, double? Confidence);

/// <summary>Linia (lub fragment) tekstu z położeniem; <see cref="Words"/> jest puste, gdy silnik nie zwraca słów.</summary>
public sealed record OcrLine(string Text, NormBox Box, double? Confidence, IReadOnlyList<OcrWord> Words);

public sealed record OcrTableCell(int Row, int Col, int RowSpan, int ColSpan, string Text, NormBox? Box);

public sealed record OcrTable(int PageNumber, NormBox? Box, int Rows, int Cols, IReadOnlyList<OcrTableCell> Cells, string? Html)
{
    /// <summary>Gęsta siatka Rows x Cols (komórki scalone mają tekst w lewym górnym polu).</summary>
    public string[][] ToGrid()
    {
        var grid = Enumerable.Range(0, Rows).Select(_ => Enumerable.Repeat(string.Empty, Cols).ToArray()).ToArray();
        foreach (var cell in Cells)
        {
            if (cell.Row < 0 || cell.Row >= Rows || cell.Col < 0 || cell.Col >= Cols || string.IsNullOrEmpty(cell.Text))
            {
                continue;
            }

            var current = grid[cell.Row][cell.Col];
            grid[cell.Row][cell.Col] = current.Length == 0 ? cell.Text : $"{current} {cell.Text}";
        }

        return grid;
    }
}

/// <param name="Number">Numer strony od 1.</param>
/// <param name="Text">Tekst strony w kolejności czytania wyznaczonej przez silnik (tabele wiersz po wierszu).</param>
/// <param name="Lines">Wszystkie rozpoznane fragmenty z położeniem – źródło warstwy tekstowej PDF.</param>
public sealed record OcrPage(int Number, string Text, IReadOnlyList<OcrLine> Lines, IReadOnlyList<OcrTable> Tables);

public sealed record OcrDocumentResult(
    string Engine,
    string EngineVersion,
    IReadOnlyList<OcrPage> Pages,
    TimeSpan Duration,
    byte[]? NativeSearchablePdf = null,
    string? RawJson = null)
{
    public string Text => string.Join("\n\n", Pages.Select(p => p.Text));
    public IEnumerable<OcrTable> Tables => Pages.SelectMany(p => p.Tables);
    public int WordCount => Pages.Sum(p => p.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length);
}
