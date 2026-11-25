using OcrDemo.Core.Metrics;

namespace OcrDemo.Tests;

public class TextMetricsTests
{
    [Fact]
    public void IdenticalTexts_AreZeroError_IgnoringWhitespaceLayout()
    {
        var q = TextMetrics.Compare("Zażółć  gęślą\njaźń", "Zażółć gęślą jaźń\n");

        Assert.Equal(0, q.Cer);
        Assert.Equal(0, q.Wer);
        Assert.Equal(1, q.WordRecall);
    }

    [Fact]
    public void MissingDiacritic_CountsAsOneCharacterAndOneWordError()
    {
        var q = TextMetrics.Compare("Łukasz Ślęzak", "Lukasz Ślęzak");

        Assert.Equal(1d / 13, q.Cer, 6);
        Assert.Equal(0.5, q.Wer, 6);
        Assert.Equal(0.5, q.WordRecall, 6);
    }

    [Fact]
    public void WordRecall_IsOrderIndependent()
    {
        var q = TextMetrics.Compare("a b c d", "c d a b");

        Assert.Equal(1, q.WordRecall);
        Assert.True(q.Wer > 0);
    }

    [Fact]
    public void Levenshtein_ClassicExample()
    {
        Assert.Equal(3, TextMetrics.Levenshtein("kitten".AsSpan(), "sitting".AsSpan()));
    }
}
