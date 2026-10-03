using Assistent.Speech;

namespace Assistent.Tests;

[TestClass]
public sealed class SpeechTests
{
    [TestMethod]
    public void Clean_RemovesCodeUrlsAndMarkdown()
    {
        var result = SpeechTextCleaner.Clean("**Olá** veja https://exemplo.com\n```\ncode\n```\nfim");
        Assert.DoesNotContain("http", result);
        Assert.DoesNotContain("code", result);
        Assert.DoesNotContain("**", result);
        Assert.Contains("Olá", result);
    }

    [TestMethod]
    public void Clean_TruncatesLongText()
    {
        Assert.IsLessThanOrEqualTo(1200, SpeechTextCleaner.Clean(new string('a', 5000)).Length);
    }

    [TestMethod]
    public void SpeechOptions_HaveDefaults()
    {
        var o = new SpeechOptions();
        Assert.AreEqual("pt-BR", o.Language);
        Assert.IsFalse(o.SpeakResponses);
    }
}