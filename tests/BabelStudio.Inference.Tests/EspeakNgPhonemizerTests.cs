using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class EspeakNgPhonemizerTests
{
    // ── Constructor validation ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultExecutablePath_DoesNotThrow()
    {
        // Should construct without error; actual process invocation is not tested here.
        var phonemizer = new EspeakNgPhonemizer();
        Assert.NotNull(phonemizer);
    }

    [Fact]
    public void Constructor_NullExecutablePath_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new EspeakNgPhonemizer(null!));
    }

    [Fact]
    public void Constructor_WhitespaceExecutablePath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new EspeakNgPhonemizer("   "));
    }

    [Fact]
    public void Constructor_EmptyExecutablePath_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new EspeakNgPhonemizer(""));
    }

    // ── Phonemize input validation ─────────────────────────────────────────────

    [Fact]
    public void Phonemize_EmptyText_ThrowsArgumentException()
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("", "en-us"));
    }

    [Fact]
    public void Phonemize_WhitespaceText_ThrowsArgumentException()
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("   ", "en-us"));
    }

    [Fact]
    public void Phonemize_EmptyLanguageCode_ThrowsArgumentException()
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("Hello world", ""));
    }

    [Fact]
    public void Phonemize_WhitespaceLanguageCode_ThrowsArgumentException()
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("Hello world", "   "));
    }

    // ── Language code pattern validation ──────────────────────────────────────

    [Theory]
    [InlineData("en")]
    [InlineData("en-us")]
    [InlineData("en_US")]
    [InlineData("zh-TW")]
    [InlineData("pt-BR")]
    [InlineData("123")]
    public void Phonemize_ValidLanguageCodePattern_DoesNotThrowValidationError(string languageCode)
    {
        // These should pass pattern validation (they may fail later if the process can't start,
        // but the ArgumentException from pattern validation must not fire).
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        Exception? thrown = Record.Exception(
            () => phonemizer.Phonemize("Hello", languageCode));

        // Should NOT be an ArgumentException (validation error) — it may be
        // Win32Exception/IOException from process launch, which is acceptable.
        Assert.True(
            thrown is null || thrown is not ArgumentException,
            $"Expected no ArgumentException but got {thrown?.GetType().Name}: {thrown?.Message}");
    }

    [Theory]
    [InlineData("en us")]          // space inside
    [InlineData("en.us")]          // dot not allowed
    [InlineData("en/us")]          // slash not allowed
    [InlineData("en;us")]          // semicolon not allowed
    [InlineData("(en)")]           // parentheses not allowed
    public void Phonemize_InvalidLanguageCodePattern_ThrowsArgumentException(string languageCode)
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("Hello", languageCode));

        Assert.Equal("languageCode", ex.ParamName);
    }

    [Fact]
    public void Phonemize_InvalidLanguageCode_ExceptionNamedCorrectly()
    {
        var phonemizer = new EspeakNgPhonemizer("espeak-ng-nonexistent");

        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => phonemizer.Phonemize("Hello", "bad language code"));

        Assert.Equal("languageCode", ex.ParamName);
    }
}