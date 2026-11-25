using System.Text;

namespace OcrDemo.Core.Metrics;

/// <param name="Cer">Character Error Rate = odległość edycyjna znaków / liczba znaków wzorca (0 = idealnie).</param>
/// <param name="Wer">Word Error Rate = odległość edycyjna słów / liczba słów wzorca (0 = idealnie).</param>
/// <param name="WordRecall">
/// Odsetek słów wzorca znalezionych w wyniku niezależnie od kolejności (1 = wszystkie).
/// Odporne na inną kolejność czytania (np. kolumny, tabele), w odróżnieniu od CER/WER.
/// </param>
public sealed record TextQuality(double Cer, double Wer, double WordRecall, int ReferenceChars, int ReferenceWords);

public static class TextMetrics
{
    public static TextQuality Compare(string reference, string hypothesis)
    {
        var refNorm = Normalize(reference);
        var hypNorm = Normalize(hypothesis);

        var refWords = SplitWords(refNorm);
        var hypWords = SplitWords(hypNorm);

        var cer = refNorm.Length == 0 ? (hypNorm.Length == 0 ? 0 : 1) : (double)Levenshtein(refNorm.AsSpan(), hypNorm.AsSpan()) / refNorm.Length;
        var wer = refWords.Length == 0 ? (hypWords.Length == 0 ? 0 : 1) : (double)Levenshtein(ToIds(refWords, hypWords, out var hypIds), hypIds) / refWords.Length;

        return new TextQuality(cer, wer, WordRecall(refWords, hypWords), refNorm.Length, refWords.Length);
    }

    /// <summary>NFC + zamiana dowolnych białych znaków (także nowych linii) na pojedynczą spację.</summary>
    public static string Normalize(string text)
    {
        var nfc = text.Normalize(NormalizationForm.FormC);
        var sb = new StringBuilder(nfc.Length);
        var space = false;
        foreach (var ch in nfc)
        {
            if (char.IsWhiteSpace(ch))
            {
                space = sb.Length > 0;
                continue;
            }

            if (space)
            {
                sb.Append(' ');
                space = false;
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string[] SplitWords(string normalized) =>
        normalized.Length == 0 ? [] : normalized.Split(' ');

    private static double WordRecall(string[] reference, string[] hypothesis)
    {
        if (reference.Length == 0)
        {
            return 1;
        }

        var available = hypothesis.GroupBy(w => w, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var found = 0;
        foreach (var word in reference)
        {
            if (available.TryGetValue(word, out var count) && count > 0)
            {
                available[word] = count - 1;
                found++;
            }
        }

        return (double)found / reference.Length;
    }

    private static int[] ToIds(string[] reference, string[] hypothesis, out int[] hypothesisIds)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        int Id(string w) => map.TryGetValue(w, out var id) ? id : map[w] = map.Count;
        var refIds = reference.Select(Id).ToArray();
        hypothesisIds = hypothesis.Select(Id).ToArray();
        return refIds;
    }

    /// <summary>Odległość Levenshteina, pamięć O(min(n, m)); wspólny prefiks i sufiks są pomijane.</summary>
    public static int Levenshtein<T>(ReadOnlySpan<T> a, ReadOnlySpan<T> b) where T : IEquatable<T>
    {
        var prefix = a.CommonPrefixLength(b);
        a = a[prefix..];
        b = b[prefix..];
        while (a.Length > 0 && b.Length > 0 && a[^1].Equals(b[^1]))
        {
            a = a[..^1];
            b = b[..^1];
        }

        if (a.Length < b.Length)
        {
            var tmp = a;
            a = b;
            b = tmp;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            var ai = a[i - 1];
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = ai.Equals(b[j - 1]) ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
