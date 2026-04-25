using BabelStudio.Inference.Onnx.Kokoro;

namespace BabelStudio.Inference.Tests;

public sealed class KokoroTokenizerTests : IDisposable
{
    private readonly string tempDir;

    public KokoroTokenizerTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), $"kokoro-tokenizer-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private void WriteTokenizerJson(string json)
    {
        File.WriteAllText(Path.Combine(tempDir, "tokenizer.json"), json);
    }

    private static string MinimalTokenizerJson(IEnumerable<(string ch, int id)> vocabEntries)
    {
        string vocabPairs = string.Join(",\n",
            vocabEntries.Select(e => $"    \"{EscapeJson(e.ch)}\": {e.id}"));
        return $$"""
            {
              "model": {
                "vocab": {
            {{vocabPairs}}
                }
              }
            }
            """;
    }

    private static string EscapeJson(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    [Fact]
    public void Load_ThrowsFileNotFoundException_WhenTokenizerJsonMissing()
    {
        Assert.Throws<FileNotFoundException>(() => KokoroTokenizer.Load(tempDir));
    }

    [Fact]
    public void Load_ThrowsFileNotFoundException_WithHelpfulMessage()
    {
        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => KokoroTokenizer.Load(tempDir));

        Assert.Contains("tokenizer.json", ex.Message);
    }

    [Fact]
    public void Encode_EmptyPhonemes_ReturnsBosAndEosOnly()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("a", 1)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("");

        Assert.Equal(2, tokens.Length);
        Assert.Equal(0L, tokens[0]); // BOS
        Assert.Equal(0L, tokens[1]); // EOS
    }

    [Fact]
    public void Encode_KnownCharacters_ReturnsBosTokensEos()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("h", 10), ("i", 20)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("hi");

        Assert.Equal(4, tokens.Length);
        Assert.Equal(0L, tokens[0]);  // BOS
        Assert.Equal(10L, tokens[1]); // 'h'
        Assert.Equal(20L, tokens[2]); // 'i'
        Assert.Equal(0L, tokens[3]);  // EOS
    }

    [Fact]
    public void Encode_UnknownCharacters_AreSkipped()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("a", 5)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        // 'b' and 'c' are unknown; only 'a' is in the vocab
        long[] tokens = tokenizer.Encode("bac");

        Assert.Equal(3, tokens.Length); // BOS + 'a' + EOS
        Assert.Equal(0L, tokens[0]);
        Assert.Equal(5L, tokens[1]);
        Assert.Equal(0L, tokens[2]);
    }

    [Fact]
    public void Encode_AllUnknownCharacters_ReturnsBosAndEosOnly()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("x", 99)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("zzz");

        Assert.Equal(2, tokens.Length);
        Assert.Equal(0L, tokens[0]);
        Assert.Equal(0L, tokens[1]);
    }

    [Fact]
    public void Encode_AlwaysStartsWithBosTokenId0()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("a", 1)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("a");

        Assert.Equal(0L, tokens[0]);
    }

    [Fact]
    public void Encode_AlwaysEndsWithEosTokenId0()
    {
        WriteTokenizerJson(MinimalTokenizerJson([("a", 1)]));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("a");

        Assert.Equal(0L, tokens[^1]);
    }

    [Fact]
    public void Encode_TruncatesAtMaxSequenceLength512()
    {
        // Build a vocab with 511 unique characters using Unicode range
        var vocabEntries = Enumerable.Range(0, 512)
            .Select(i => (ch: ((char)(0x0100 + i)).ToString(), id: i + 1))
            .ToList();
        WriteTokenizerJson(MinimalTokenizerJson(vocabEntries));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        // Input of 600 characters — all known, exceeds max of 512
        string input = new string(Enumerable.Range(0, 600).Select(i => vocabEntries[i % vocabEntries.Count].ch[0]).ToArray());
        long[] tokens = tokenizer.Encode(input);

        // Must be capped: [BOS, ...510 tokens..., EOS] = 512
        Assert.Equal(512, tokens.Length);
        Assert.Equal(0L, tokens[0]);
        Assert.Equal(0L, tokens[^1]);
    }

    [Fact]
    public void Encode_InputWithinMaxLength_NotTruncated()
    {
        var vocabEntries = Enumerable.Range(0, 26)
            .Select(i => (ch: ((char)('a' + i)).ToString(), id: i + 1))
            .ToList();
        WriteTokenizerJson(MinimalTokenizerJson(vocabEntries));
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        string input = "hello"; // 5 chars, all known
        long[] tokens = tokenizer.Encode(input);

        // [BOS, h, e, l, l, o, EOS] = 7
        Assert.Equal(7, tokens.Length);
    }

    [Fact]
    public void Load_IgnoresNonIntegerVocabValues()
    {
        // Vocabulary entry with a non-integer value should be silently ignored
        string json = """
            {
              "model": {
                "vocab": {
                  "a": 1,
                  "b": "not_an_int"
                }
              }
            }
            """;
        WriteTokenizerJson(json);
        KokoroTokenizer tokenizer = KokoroTokenizer.Load(tempDir);

        long[] tokens = tokenizer.Encode("ab");

        // 'b' was ignored, only 'a' is tokenized
        Assert.Equal(3, tokens.Length); // BOS + a + EOS
    }
}