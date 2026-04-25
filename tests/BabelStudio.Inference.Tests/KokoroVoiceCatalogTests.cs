using BabelStudio.Contracts.Pipeline;
using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroVoiceCatalogTests : IDisposable
{
    private readonly List<string> tempDirectories = [];

    public void Dispose()
    {
        foreach (string dir in tempDirectories)
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    // ── Load ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Load_NoVoicesDirectory_ReturnsEmptyCatalog()
    {
        string root = CreateTempModelRoot();

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Empty(catalog.GetVoices());
    }

    [Fact]
    public void Load_EmptyVoicesDirectory_ReturnsEmptyCatalog()
    {
        string root = CreateTempModelRoot();
        Directory.CreateDirectory(Path.Combine(root, "voices"));

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Empty(catalog.GetVoices());
    }

    [Fact]
    public void Load_ValidBinFiles_ParsesEntries()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");
        CreateFakeVoicepackBin(root, "am_adam");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Equal(2, catalog.GetVoices().Count);
    }

    [Fact]
    public void Load_SkipsFilesWithInvalidNamingFormat()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "invalid");      // too short
        CreateFakeVoicepackBin(root, "ab");            // too short (< 3 chars)
        CreateFakeVoicepackBin(root, "abXhello");      // underscore not at index 2
        CreateFakeVoicepackBin(root, "af_heart");      // valid

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Single(catalog.GetVoices());
        Assert.Equal("af_heart", catalog.GetVoices()[0].VoiceId);
    }

    [Fact]
    public void Load_NonBinFilesAreIgnored()
    {
        string root = CreateTempModelRoot();
        string voicesDir = Path.Combine(root, "voices");
        Directory.CreateDirectory(voicesDir);
        File.WriteAllBytes(Path.Combine(voicesDir, "af_heart.json"), []);
        File.WriteAllBytes(Path.Combine(voicesDir, "am_adam.txt"), []);
        CreateFakeVoicepackBin(root, "bf_alice");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Single(catalog.GetVoices());
    }

    [Fact]
    public void Load_VoicesAreSortedByVoiceId()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "zm_zhang");
        CreateFakeVoicepackBin(root, "af_heart");
        CreateFakeVoicepackBin(root, "bm_george");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();
        Assert.Equal("af_heart", voices[0].VoiceId);
        Assert.Equal("bm_george", voices[1].VoiceId);
        Assert.Equal("zm_zhang", voices[2].VoiceId);
    }

    // ── Locale prefix parsing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("af_heart", "en-us")]
    [InlineData("bf_alice", "en-gb")]
    [InlineData("ef_rosa", "es")]
    [InlineData("ff_camille", "fr")]
    [InlineData("hf_ananya", "hi")]
    [InlineData("if_lucia", "it")]
    [InlineData("jf_hana", "ja")]
    [InlineData("kf_mina", "ko")]
    [InlineData("pf_ana", "pt")]
    [InlineData("rf_daria", "ru")]
    [InlineData("zf_xiaoyi", "zh")]
    public void Load_ParsesLocalePrefix(string voiceId, string expectedLanguageCode)
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, voiceId);

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        VoiceCatalogEntry entry = Assert.Single(catalog.GetVoices());
        Assert.Equal(expectedLanguageCode, entry.LanguageCode);
    }

    [Fact]
    public void Load_UnknownLocalePrefix_MapsToUnknown()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "xf_mystery");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        VoiceCatalogEntry entry = Assert.Single(catalog.GetVoices());
        Assert.Equal("unknown", entry.LanguageCode);
    }

    // ── Gender prefix parsing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("af_heart", "female")]
    [InlineData("am_adam", "male")]
    public void Load_ParsesGenderFromSecondChar(string voiceId, string expectedGender)
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, voiceId);

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        VoiceCatalogEntry entry = Assert.Single(catalog.GetVoices());
        Assert.Equal(expectedGender, entry.Gender);
    }

    [Fact]
    public void Load_UnknownGenderChar_MapsToUnknown()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "ax_mystery");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        VoiceCatalogEntry entry = Assert.Single(catalog.GetVoices());
        Assert.Equal("unknown", entry.Gender);
    }

    // ── DisplayName parsing ───────────────────────────────────────────────────

    [Theory]
    [InlineData("af_heart", "Heart")]
    [InlineData("am_adam", "Adam")]
    [InlineData("bm_george", "George")]
    [InlineData("af_sky_blue", "Sky Blue")]   // underscores become spaces, title-cased
    public void Load_ParsesDisplayName(string voiceId, string expectedDisplayName)
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, voiceId);

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        VoiceCatalogEntry entry = Assert.Single(catalog.GetVoices());
        Assert.Equal(expectedDisplayName, entry.DisplayName);
    }

    // ── GetVoices ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetVoices_NoLanguageFilter_ReturnsAll()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");
        CreateFakeVoicepackBin(root, "bf_alice");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Equal(2, catalog.GetVoices().Count);
    }

    [Fact]
    public void GetVoices_NullLanguageCode_ReturnsAll()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");
        CreateFakeVoicepackBin(root, "bf_alice");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Equal(2, catalog.GetVoices(null).Count);
    }

    [Fact]
    public void GetVoices_FiltersByLanguageCode()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");   // en-us
        CreateFakeVoicepackBin(root, "am_adam");    // en-us
        CreateFakeVoicepackBin(root, "bf_alice");   // en-gb

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        IReadOnlyList<VoiceCatalogEntry> enUs = catalog.GetVoices("en-us");
        Assert.Equal(2, enUs.Count);
        Assert.All(enUs, v => Assert.Equal("en-us", v.LanguageCode));
    }

    [Fact]
    public void GetVoices_LanguageWithNoMatches_ReturnsEmpty()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");   // en-us

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        Assert.Empty(catalog.GetVoices("zh"));
    }

    // ── TryGetVoice ───────────────────────────────────────────────────────────

    [Fact]
    public void TryGetVoice_KnownVoiceId_ReturnsTrueAndEntry()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        bool found = catalog.TryGetVoice("af_heart", out VoiceCatalogEntry? entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("af_heart", entry.VoiceId);
    }

    [Fact]
    public void TryGetVoice_UnknownVoiceId_ReturnsFalse()
    {
        string root = CreateTempModelRoot();
        CreateFakeVoicepackBin(root, "af_heart");

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        bool found = catalog.TryGetVoice("xx_nonexistent", out VoiceCatalogEntry? entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    [Fact]
    public void TryGetVoice_EmptyCatalog_ReturnsFalse()
    {
        string root = CreateTempModelRoot();

        KokoroVoiceCatalog catalog = KokoroVoiceCatalog.Load(root);

        bool found = catalog.TryGetVoice("af_heart", out VoiceCatalogEntry? entry);

        Assert.False(found);
        Assert.Null(entry);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string CreateTempModelRoot()
    {
        string dir = Path.Combine(
            Path.GetTempPath(),
            "BabelStudio.KokoroVoiceCatalogTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        tempDirectories.Add(dir);
        return dir;
    }

    private static void CreateFakeVoicepackBin(string modelRoot, string voiceId)
    {
        string voicesDir = Path.Combine(modelRoot, "voices");
        Directory.CreateDirectory(voicesDir);
        File.WriteAllBytes(Path.Combine(voicesDir, $"{voiceId}.bin"), []);
    }
}