using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroHelperComponentTests : IDisposable
{
    private readonly List<string> tempDirs = [];

    public void Dispose()
    {
        foreach (string dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── KokoroTokenizer ────────────────────────────────────────────────────────

    [Fact]
    public void KokoroTokenizer_Encode_WrapsWithBosEos()
    {
        string dir = CreateTempDir();
        WriteMinimalTokenizerJson(dir, new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
        var tokenizer = KokoroTokenizer.Load(dir);

        long[] tokens = tokenizer.Encode("ab");

        // BOS(0) + 'a'(1) + 'b'(2) + EOS(0)
        Assert.Equal(4, tokens.Length);
        Assert.Equal(0L, tokens[0]);
        Assert.Equal(1L, tokens[1]);
        Assert.Equal(2L, tokens[2]);
        Assert.Equal(0L, tokens[3]);
    }

    [Fact]
    public void KokoroTokenizer_Encode_SkipsUnknownChars()
    {
        string dir = CreateTempDir();
        WriteMinimalTokenizerJson(dir, new Dictionary<string, int> { ["a"] = 5 });
        var tokenizer = KokoroTokenizer.Load(dir);

        long[] tokens = tokenizer.Encode("aXa");

        // BOS(0) + 'a'(5) + skip 'X' + 'a'(5) + EOS(0)
        Assert.Equal(4, tokens.Length);
        Assert.Equal(5L, tokens[1]);
        Assert.Equal(5L, tokens[2]);
    }

    [Fact]
    public void KokoroTokenizer_Encode_EmptyText_ReturnsBosEosOnly()
    {
        string dir = CreateTempDir();
        WriteMinimalTokenizerJson(dir, new Dictionary<string, int> { ["a"] = 1 });
        var tokenizer = KokoroTokenizer.Load(dir);

        long[] tokens = tokenizer.Encode(" "); // single space has no vocab entry

        Assert.Equal(2, tokens.Length);
        Assert.Equal(0L, tokens[0]);
        Assert.Equal(0L, tokens[1]);
    }

    [Fact]
    public void KokoroTokenizer_Load_ThrowsWhenFileAbsent()
    {
        string dir = CreateTempDir(); // no tokenizer.json

        Assert.Throws<FileNotFoundException>(() => KokoroTokenizer.Load(dir));
    }

    // ─── KokoroPcmConverter ─────────────────────────────────────────────────────

    [Fact]
    public void KokoroPcmConverter_EncodePcm16Wav_HasCorrectRiffHeader()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([], sampleRate: 24_000);

        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);

        Assert.Equal((byte)'W', wav[8]);
        Assert.Equal((byte)'A', wav[9]);
        Assert.Equal((byte)'V', wav[10]);
        Assert.Equal((byte)'E', wav[11]);

        Assert.Equal((byte)'f', wav[12]);
        Assert.Equal((byte)'m', wav[13]);
        Assert.Equal((byte)'t', wav[14]);
        Assert.Equal((byte)' ', wav[15]);

        Assert.Equal((byte)'d', wav[36]);
        Assert.Equal((byte)'a', wav[37]);
        Assert.Equal((byte)'t', wav[38]);
        Assert.Equal((byte)'a', wav[39]);
    }

    [Fact]
    public void KokoroPcmConverter_EncodePcm16Wav_EmptySamples_Returns44ByteHeader()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([], sampleRate: 24_000);

        Assert.Equal(44, wav.Length);
    }

    [Fact]
    public void KokoroPcmConverter_EncodePcm16Wav_LengthMatchesSampleCount()
    {
        float[] samples = new float[100];
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav(samples, sampleRate: 24_000);

        Assert.Equal(44 + 100 * sizeof(short), wav.Length);
    }

    [Fact]
    public void KokoroPcmConverter_EncodePcm16Wav_PositiveFullScaleSampleClampsTo32767()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([1.0f], sampleRate: 24_000);

        short sample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44));
        Assert.Equal((short)32_767, sample);
    }

    [Fact]
    public void KokoroPcmConverter_EncodePcm16Wav_NegativeFullScaleSampleClampsToMinusMaxValue()
    {
        byte[] wav = KokoroPcmConverter.EncodePcm16Wav([-1.0f], sampleRate: 24_000);

        short sample = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44));
        Assert.Equal((short)-32_767, sample);
    }

    // ─── KokoroVoicepackLoader ──────────────────────────────────────────────────

    [Fact]
    public void KokoroVoicepackLoader_LoadStyleVector_ReadsCorrectRow()
    {
        const int StyleVectorSize = 256;
        string binPath = Path.GetTempFileName();
        try
        {
            float[] row0 = Enumerable.Range(0, StyleVectorSize).Select(i => (float)i).ToArray();
            float[] row1 = Enumerable.Range(1000, StyleVectorSize).Select(i => (float)i).ToArray();
            using (var writer = new BinaryWriter(File.OpenWrite(binPath)))
            {
                foreach (float v in row0) writer.Write(v);
                foreach (float v in row1) writer.Write(v);
            }

            float[] loaded0 = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 0);
            Assert.Equal(StyleVectorSize, loaded0.Length);
            Assert.Equal(0f, loaded0[0]);
            Assert.Equal(255f, loaded0[255]);

            float[] loaded1 = KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 1);
            Assert.Equal(1000f, loaded1[0]);
            Assert.Equal(1255f, loaded1[255]);
        }
        finally
        {
            File.Delete(binPath);
        }
    }

    [Fact]
    public void KokoroVoicepackLoader_LoadStyleVector_ThrowsWhenFileTooSmall()
    {
        string binPath = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(binPath, [1, 2, 3]); // too small for one style vector

            Assert.Throws<InvalidOperationException>(() =>
                KokoroVoicepackLoader.LoadStyleVector(binPath, tokenCount: 0));
        }
        finally
        {
            File.Delete(binPath);
        }
    }

    // ─── KokoroVoiceCatalog ─────────────────────────────────────────────────────

    [Fact]
    public void KokoroVoiceCatalog_Load_ReturnsEmptyWhenVoicesDirAbsent()
    {
        string dir = CreateTempDir(); // no "voices" subdirectory

        var catalog = KokoroVoiceCatalog.Load(dir);

        Assert.Empty(catalog.GetVoices());
    }

    [Fact]
    public void KokoroVoiceCatalog_Load_ParsesVoiceFilesCorrectly()
    {
        string dir = CreateTempDir();
        CreateVoicesDir(dir, "af_heart.bin", "bm_george.bin");

        var catalog = KokoroVoiceCatalog.Load(dir);
        IReadOnlyList<VoiceCatalogEntry> voices = catalog.GetVoices();

        Assert.Equal(2, voices.Count);
        Assert.Contains(voices, v => v.VoiceId == "af_heart" && v.LanguageCode == "en-us" && v.Gender == "female");
        Assert.Contains(voices, v => v.VoiceId == "bm_george" && v.LanguageCode == "en-gb" && v.Gender == "male");
    }

    [Fact]
    public void KokoroVoiceCatalog_Load_IgnoresFilesWithUnrecognizedNamingConvention()
    {
        string dir = CreateTempDir();
        CreateVoicesDir(dir, "af_heart.bin", "invalid.bin");

        var catalog = KokoroVoiceCatalog.Load(dir);

        Assert.Single(catalog.GetVoices());
        Assert.Equal("af_heart", catalog.GetVoices()[0].VoiceId);
    }

    [Fact]
    public void KokoroVoiceCatalog_GetVoices_FiltersByLanguageCode()
    {
        string dir = CreateTempDir();
        CreateVoicesDir(dir, "af_heart.bin", "ef_dora.bin");

        var catalog = KokoroVoiceCatalog.Load(dir);
        IReadOnlyList<VoiceCatalogEntry> enUs = catalog.GetVoices("en-us");

        Assert.Single(enUs);
        Assert.Equal("af_heart", enUs[0].VoiceId);
    }

    [Fact]
    public void KokoroVoiceCatalog_TryGetVoice_ReturnsTrueForKnownVoice()
    {
        string dir = CreateTempDir();
        CreateVoicesDir(dir, "af_heart.bin");

        var catalog = KokoroVoiceCatalog.Load(dir);
        bool found = catalog.TryGetVoice("af_heart", out VoiceCatalogEntry? entry);

        Assert.True(found);
        Assert.NotNull(entry);
        Assert.Equal("en-us", entry.LanguageCode);
        Assert.Equal("female", entry.Gender);
    }

    [Fact]
    public void KokoroVoiceCatalog_TryGetVoice_ReturnsFalseForUnknownVoice()
    {
        string dir = CreateTempDir();
        CreateVoicesDir(dir); // empty voices dir

        var catalog = KokoroVoiceCatalog.Load(dir);

        Assert.False(catalog.TryGetVoice("af_heart", out _));
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        tempDirs.Add(dir);
        return dir;
    }

    private static void CreateVoicesDir(string modelRoot, params string[] binFileNames)
    {
        string voicesDir = Path.Combine(modelRoot, "voices");
        Directory.CreateDirectory(voicesDir);
        foreach (string name in binFileNames)
        {
            File.WriteAllBytes(Path.Combine(voicesDir, name), []);
        }
    }

    private static void WriteMinimalTokenizerJson(string dir, Dictionary<string, int> vocab)
    {
        var obj = new { model = new { vocab } };
        string json = JsonSerializer.Serialize(obj);
        File.WriteAllText(Path.Combine(dir, "tokenizer.json"), json, Encoding.UTF8);
    }
}
