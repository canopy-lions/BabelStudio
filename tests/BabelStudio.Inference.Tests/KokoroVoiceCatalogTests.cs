using BabelStudio.Contracts.Pipeline;
using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroVoiceCatalogTests : IDisposable
{
    private readonly string modelRoot;
    private readonly string voicesDir;

    public KokoroVoiceCatalogTests()
    {
        modelRoot = Path.Combine(Path.GetTempPath(), $"kokoro-catalog-tests-{Guid.NewGuid():N}");
        voicesDir = Path.Combine(modelRoot, "voices");
        Directory.CreateDirectory(voicesDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(modelRoot))
        {
            Directory.Delete(modelRoot, recursive: true);
        }
    }

    private void CreateFakeVoiceBin(string voiceId)
    {
        File.WriteAllBytes(Path.Combine(voicesDir, $"{voiceId}.bin"), []);
    }

    [Fact]
    public void Load_ReturnsEmptyCatalog_WhenVoicesDirectoryMissing()
    {
        string rootWithoutVoices = Path.Combine(Path.GetTempPath(), $"no-voices-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootWithoutVoices);
        try
        {
            KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(rootWithoutVoices);
            Assert.Empty(catalog.GetVoices());
        }
        finally
        {
            Directory.Delete(rootWithoutVoices, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsEmptyCatalog_WhenVoicesDirIsEmpty()
    {
        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        Assert.Empty(catalog.GetVoices());
    }

    [Fact]
    public void Load_ParsesAmericanEnglishFemaleVoice()
    {
        CreateFakeVoiceBin("af_heart");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal("af_heart", voices[0].VoiceId);
        Assert.Equal("en-us", voices[0].LanguageCode);
        Assert.Equal("female", voices[0].Gender);
        Assert.Equal("Heart", voices[0].DisplayName);
    }

    [Fact]
    public void Load_ParsesBritishEnglishMaleVoice()
    {
        CreateFakeVoiceBin("bm_george");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal("bm_george", voices[0].VoiceId);
        Assert.Equal("en-gb", voices[0].LanguageCode);
        Assert.Equal("male", voices[0].Gender);
        Assert.Equal("George", voices[0].DisplayName);
    }

    [Theory]
    [InlineData("a", "en-us")]
    [InlineData("b", "en-gb")]
    [InlineData("e", "es")]
    [InlineData("f", "fr")]
    [InlineData("h", "hi")]
    [InlineData("i", "it")]
    [InlineData("j", "ja")]
    [InlineData("k", "ko")]
    [InlineData("p", "pt")]
    [InlineData("r", "ru")]
    [InlineData("z", "zh")]
    public void Load_LocalePrefix_MapsToCorrectLanguageCode(string localePrefix, string expectedLanguageCode)
    {
        CreateFakeVoiceBin($"{localePrefix}f_test");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal(expectedLanguageCode, voices[0].LanguageCode);
    }

    [Fact]
    public void Load_UnknownLocalePrefix_MapsToUnknown()
    {
        CreateFakeVoiceBin("xf_mystery");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal("unknown", voices[0].LanguageCode);
    }

    [Theory]
    [InlineData("f", "female")]
    [InlineData("m", "male")]
    public void Load_GenderChar_MapsToCorrectGender(string genderChar, string expectedGender)
    {
        CreateFakeVoiceBin($"a{genderChar}_test");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal(expectedGender, voices[0].Gender);
    }

    [Fact]
    public void Load_UnknownGenderChar_MapsToUnknown()
    {
        CreateFakeVoiceBin("ax_test");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Single(voices);
        Assert.Equal("unknown", voices[0].Gender);
    }

    [Fact]
    public void Load_DisplayName_TitleCasesNamePart()
    {
        CreateFakeVoiceBin("af_heart");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        Assert.Equal("Heart", catalog.GetVoices()[0].DisplayName);
    }

    [Fact]
    public void Load_DisplayName_ReplaceUnderscoreWithSpace()
    {
        CreateFakeVoiceBin("af_warm_lady");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        // "warm_lady" => "Warm Lady"
        Assert.Equal("Warm Lady", catalog.GetVoices()[0].DisplayName);
    }

    [Fact]
    public void Load_SkipsFilesWithInvalidNamingConvention_TooShort()
    {
        CreateFakeVoiceBin("af"); // too short, needs at least 3 chars with underscore at [2]
        CreateFakeVoiceBin("af_valid");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        // Only the valid entry should be present
        Assert.Single(catalog.GetVoices());
        Assert.Equal("af_valid", catalog.GetVoices()[0].VoiceId);
    }

    [Fact]
    public void Load_SkipsFilesWithoutUnderscore_AtPosition2()
    {
        CreateFakeVoiceBin("afX_invalid"); // underscore at position 3, not 2
        CreateFakeVoiceBin("af_valid");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        Assert.Single(catalog.GetVoices());
        Assert.Equal("af_valid", catalog.GetVoices()[0].VoiceId);
    }

    [Fact]
    public void Load_OrdersVoicesAlphabeticallyByVoiceId()
    {
        CreateFakeVoiceBin("bm_george");
        CreateFakeVoiceBin("af_heart");
        CreateFakeVoiceBin("am_adam");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Equal("af_heart", voices[0].VoiceId);
        Assert.Equal("am_adam", voices[1].VoiceId);
        Assert.Equal("bm_george", voices[2].VoiceId);
    }

    [Fact]
    public void GetVoices_WithNullLanguageCode_ReturnsAllVoices()
    {
        CreateFakeVoiceBin("af_heart");
        CreateFakeVoiceBin("bm_george");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        Assert.Equal(2, catalog.GetVoices(null).Count);
    }

    [Fact]
    public void GetVoices_WithLanguageFilter_ReturnsOnlyMatchingVoices()
    {
        CreateFakeVoiceBin("af_heart"); // en-us
        CreateFakeVoiceBin("am_adam");  // en-us
        CreateFakeVoiceBin("bm_george"); // en-gb

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        IReadOnlyList<VoiceCatalogEntry> enUsVoices = catalog.GetVoices("en-us");
        Assert.Equal(2, enUsVoices.Count);
        Assert.All(enUsVoices, v => Assert.Equal("en-us", v.LanguageCode));
    }

    [Fact]
    public void GetVoices_WithNonMatchingLanguageFilter_ReturnsEmpty()
    {
        CreateFakeVoiceBin("af_heart"); // en-us

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices("zh");
        Assert.Empty(voices);
    }

    [Fact]
    public void TryGetVoice_ReturnsTrue_ForKnownVoiceId()
    {
        CreateFakeVoiceBin("af_heart");
        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        bool found = catalog.TryGetVoice("af_heart", out VoiceCatalogEntry? entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("af_heart", entry.VoiceId);
    }

    [Fact]
    public void TryGetVoice_ReturnsFalse_ForUnknownVoiceId()
    {
        CreateFakeVoiceBin("af_heart");
        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        bool found = catalog.TryGetVoice("nonexistent_voice", out VoiceCatalogEntry? entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void GetBinPath_ReturnsNull_WhenBinFileNotOnDisk()
    {
        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        // Using the internal method - accessible via InternalsVisibleTo
        string? binPath = catalog.GetBinPath("af_ghost");

        Assert.Null(binPath);
    }

    [Fact]
    public void GetBinPath_ReturnsPath_WhenBinFileExists()
    {
        CreateFakeVoiceBin("af_heart");
        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(modelRoot);

        string? binPath = catalog.GetBinPath("af_heart");

        Assert.NotNull(binPath);
        Assert.True(File.Exists(binPath));
        Assert.EndsWith("af_heart.bin", binPath);
    }
}