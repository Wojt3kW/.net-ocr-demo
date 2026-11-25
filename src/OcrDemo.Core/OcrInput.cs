namespace OcrDemo.Core;

public enum InputKind
{
    Pdf,
    Image,
}

/// <summary>
/// Dokument wejściowy w dwóch postaciach:
/// <list type="bullet">
/// <item><see cref="OriginalBytes"/> – plik źródłowy bez zmian (silniki przyjmujące obrazy dostają oryginał),</item>
/// <item><see cref="BasePdf"/> – PDF, na który nakładana jest warstwa tekstowa (dla PDF = oryginał,
/// dla obrazów = strony z obrazami w oryginalnej rozdzielczości).</item>
/// </list>
/// </summary>
public sealed record OcrInput(
    string FilePath,
    InputKind Kind,
    byte[] OriginalBytes,
    byte[] BasePdf,
    int PageCount)
{
    public string FileName => Path.GetFileName(FilePath);
    public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
}
