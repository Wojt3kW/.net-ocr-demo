namespace OcrDemo.Runner;

public sealed class RunnerOptions
{
    public const string Section = "Runner";

    /// <summary>Katalog z dokumentami (PDF, PNG, JPG, TIFF, BMP) i opcjonalnymi wzorcami &lt;nazwa&gt;.gt.txt.</summary>
    public string InputDirectory { get; set; } = "input_documents";

    /// <summary>Katalog wyników – każde uruchomienie tworzy podkatalog z datą i godziną.</summary>
    public string OutputDirectory { get; set; } = "output_documents";

    /// <summary>Lista nazw silników do uruchomienia, rozdzielona przecinkami (pusta = wszystkie włączone).</summary>
    public string? EngineFilter { get; set; }

    /// <summary>DPI przyjmowane dla obrazów bez zapisanej rozdzielczości (wpływa tylko na rozmiar strony PDF).</summary>
    public double FallbackImageDpi { get; set; } = 300;

    /// <summary>Czcionka TTF z polskimi znakami dla warstwy tekstowej (puste = DejaVu Sans / Arial).</summary>
    public string? FontPath { get; set; }

    /// <summary>
    /// Rozgrzewka: przed pomiarami każdy silnik przetwarza 1. stronę pierwszego dokumentu (bez pomiaru),
    /// aby czas pierwszego dokumentu nie zawierał leniwego ładowania modeli.
    /// </summary>
    public bool Warmup { get; set; } = true;

    /// <summary>Zapisuj surową odpowiedź silnika (&lt;silnik&gt;.raw.json) – przydatne do diagnostyki.</summary>
    public bool SaveRawOutput { get; set; } = true;
}
