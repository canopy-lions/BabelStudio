using BabelStudio.Contracts.Pipeline;
using BabelStudio.TestDoubles;

namespace BabelStudio.Application.Tests;

public sealed class FakeTtsEngineTests
{
    private static VoiceCatalogEntry TestVoice => new("af_heart", "en-us", "female", "Heart");

    [Fact]
    public async Task FakeTtsEngine_RecordsLastInputAndVoice()
    {
        var engine = new FakeTtsEngine();
        var request = new TtsSynthesisRequest("Hello world", "en-us", TestVoice);

        await engine.SynthesizeAsync(request, CancellationToken.None);

        Assert.Equal("Hello world", engine.LastInputText);
        Assert.Equal("af_heart", engine.LastVoicepack?.VoiceId);
    }

    [Fact]
    public async Task FakeTtsEngine_ReturnsValidWavBytes()
    {
        var engine = new FakeTtsEngine();
        var request = new TtsSynthesisRequest("Test", "en-us", TestVoice);

        TtsSynthesisResult result = await engine.SynthesizeAsync(request, CancellationToken.None);

        // RIFF header check
        Assert.True(result.WavBytes.Length >= 44);
        Assert.Equal((byte)'R', result.WavBytes[0]);
        Assert.Equal((byte)'I', result.WavBytes[1]);
        Assert.Equal((byte)'F', result.WavBytes[2]);
        Assert.Equal((byte)'F', result.WavBytes[3]);
        Assert.Equal((byte)'W', result.WavBytes[8]);
        Assert.Equal((byte)'A', result.WavBytes[9]);
        Assert.Equal((byte)'V', result.WavBytes[10]);
        Assert.Equal((byte)'E', result.WavBytes[11]);
    }

    [Fact]
    public async Task FakeTtsEngine_ReturnsExpectedMetadata()
    {
        var engine = new FakeTtsEngine();
        var request = new TtsSynthesisRequest("Test", "en-us", TestVoice);

        TtsSynthesisResult result = await engine.SynthesizeAsync(request, CancellationToken.None);

        Assert.Equal(24000, result.SampleRate);
        Assert.Equal(240, result.DurationSamples);
        Assert.Equal("af_heart", result.VoiceId);
        Assert.Equal("fake", result.Provider);
    }

    [Fact]
    public void FakeVoiceCatalog_GetVoices_ReturnsAllByDefault()
    {
        var catalog = new FakeVoiceCatalog();

        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Equal(3, voices.Count);
    }

    [Fact]
    public void FakeVoiceCatalog_GetVoices_FiltersbyLanguage()
    {
        var catalog = new FakeVoiceCatalog();

        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices("en-gb");

        Assert.Single(voices);
        Assert.Equal("bf_alice", voices[0].VoiceId);
    }

    [Fact]
    public void FakeVoiceCatalog_TryGetVoice_ReturnsTrueForKnownVoice()
    {
        var catalog = new FakeVoiceCatalog();

        bool found = catalog.TryGetVoice("am_adam", out VoiceCatalogEntry? entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("male", entry.Gender);
    }

    [Fact]
    public void FakePhonemizer_ReturnsFixedPhonemes()
    {
        var phonemizer = new FakePhonemizer("t@st");

        string result = phonemizer.Phonemize("anything", "en-us");

        Assert.Equal("t@st", result);
    }
}
