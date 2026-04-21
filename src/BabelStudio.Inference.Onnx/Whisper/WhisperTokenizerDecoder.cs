using System.Text;
using System.Text.Json;

namespace BabelStudio.Inference.Onnx.Whisper;

internal sealed class WhisperTokenizerDecoder
{
    private readonly IReadOnlyDictionary<int, string> tokenTextById;
    private readonly IReadOnlySet<int> suppressedTokens;
    private readonly IReadOnlyList<int> initialPromptTokens;
    private readonly IReadOnlyDictionary<char, byte> byteDecoder;
    private readonly int endOfTranscriptToken;
    private readonly int timestampBeginToken;

    private WhisperTokenizerDecoder(
        IReadOnlyDictionary<int, string> tokenTextById,
        IReadOnlySet<int> suppressedTokens,
        IReadOnlyList<int> initialPromptTokens,
        IReadOnlyDictionary<char, byte> byteDecoder,
        int endOfTranscriptToken,
        int timestampBeginToken)
    {
        this.tokenTextById = tokenTextById;
        this.suppressedTokens = suppressedTokens;
        this.initialPromptTokens = initialPromptTokens;
        this.byteDecoder = byteDecoder;
        this.endOfTranscriptToken = endOfTranscriptToken;
        this.timestampBeginToken = timestampBeginToken;
    }

    public IReadOnlyList<int> InitialPromptTokens => initialPromptTokens;

    public int EndOfTranscriptToken => endOfTranscriptToken;

    public IReadOnlySet<int> SuppressedTokens => suppressedTokens;

    public static WhisperTokenizerDecoder Load(string modelRootPath)
    {
        string tokenizerConfigPath = Path.Combine(modelRootPath, "tokenizer_config.json");
        string vocabPath = Path.Combine(modelRootPath, "vocab.json");
        string configPath = Path.Combine(modelRootPath, "config.json");

        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException("Whisper vocabulary was not found.", vocabPath);
        }

        Dictionary<int, string> tokenTextById = LoadTokenTexts(vocabPath);
        WhisperModelConfig config = LoadConfig(configPath);
        int timestampBeginToken = tokenTextById
            .Where(static pair => pair.Value.Equals("<|0.00|>", StringComparison.Ordinal))
            .Select(static pair => pair.Key)
            .DefaultIfEmpty(50364)
            .First();

        var suppressed = new HashSet<int>(config.SuppressTokens);
        foreach (int token in config.BeginSuppressTokens)
        {
            suppressed.Add(token);
        }

        if (File.Exists(tokenizerConfigPath))
        {
            using JsonDocument tokenizerConfig = JsonDocument.Parse(File.ReadAllText(tokenizerConfigPath));
            if (tokenizerConfig.RootElement.TryGetProperty("added_tokens_decoder", out JsonElement decoderElement))
            {
                foreach (JsonProperty property in decoderElement.EnumerateObject())
                {
                    if (!int.TryParse(property.Name, out int tokenId))
                    {
                        continue;
                    }

                    if (property.Value.TryGetProperty("content", out JsonElement contentElement))
                    {
                        tokenTextById[tokenId] = contentElement.GetString() ?? string.Empty;
                    }
                }
            }
        }

        List<int> promptTokens = [ config.DecoderStartTokenId ];
        foreach (int token in config.ForcedDecoderIds)
        {
            if (token != config.DecoderStartTokenId)
            {
                promptTokens.Add(token);
            }
        }

        return new WhisperTokenizerDecoder(
            tokenTextById,
            suppressed,
            promptTokens,
            BuildByteDecoder(),
            config.EndOfTranscriptTokenId,
            timestampBeginToken);
    }

    public string DecodeText(IEnumerable<int> tokenIds)
    {
        var bytes = new List<byte>();
        foreach (int tokenId in tokenIds)
        {
            if (tokenId >= timestampBeginToken || tokenId == endOfTranscriptToken)
            {
                continue;
            }

            if (!tokenTextById.TryGetValue(tokenId, out string? tokenText))
            {
                continue;
            }

            if (tokenText.StartsWith("<|", StringComparison.Ordinal) &&
                tokenText.EndsWith("|>", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (char ch in tokenText)
            {
                if (byteDecoder.TryGetValue(ch, out byte decodedByte))
                {
                    bytes.Add(decodedByte);
                }
            }
        }

        return Encoding.UTF8.GetString(bytes.ToArray()).Trim();
    }

    private static Dictionary<int, string> LoadTokenTexts(string vocabPath)
    {
        using JsonDocument vocabDocument = JsonDocument.Parse(File.ReadAllText(vocabPath));
        var tokenTextById = new Dictionary<int, string>();
        foreach (JsonProperty property in vocabDocument.RootElement.EnumerateObject())
        {
            int tokenId = property.Value.GetInt32();
            tokenTextById[tokenId] = property.Name;
        }

        return tokenTextById;
    }

    private static WhisperModelConfig LoadConfig(string configPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
        JsonElement root = document.RootElement;

        int decoderStartTokenId = root.GetProperty("decoder_start_token_id").GetInt32();
        int endOfTranscriptTokenId = root.GetProperty("eos_token_id").GetInt32();

        List<int> forcedDecoderIds = [];
        if (root.TryGetProperty("forced_decoder_ids", out JsonElement forcedElement))
        {
            foreach (JsonElement pair in forcedElement.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() < 2)
                {
                    continue;
                }

                forcedDecoderIds.Add(pair[1].GetInt32());
            }
        }

        List<int> suppressTokens = root.TryGetProperty("suppress_tokens", out JsonElement suppressElement)
            ? suppressElement.EnumerateArray().Select(static element => element.GetInt32()).ToList()
            : [];

        List<int> beginSuppressTokens = root.TryGetProperty("begin_suppress_tokens", out JsonElement beginSuppressElement)
            ? beginSuppressElement.EnumerateArray().Select(static element => element.GetInt32()).ToList()
            : [];

        return new WhisperModelConfig(
            decoderStartTokenId,
            endOfTranscriptTokenId,
            forcedDecoderIds,
            suppressTokens,
            beginSuppressTokens);
    }

    private static IReadOnlyDictionary<char, byte> BuildByteDecoder()
    {
        List<int> byteValues =
        [
            .. Enumerable.Range(33, 94),
            .. Enumerable.Range(161, 12),
            .. Enumerable.Range(174, 82)
        ];

        List<int> unicodeValues = [.. byteValues];
        int extra = 0;
        for (int byteValue = 0; byteValue < 256; byteValue++)
        {
            if (byteValues.Contains(byteValue))
            {
                continue;
            }

            byteValues.Add(byteValue);
            unicodeValues.Add(256 + extra);
            extra++;
        }

        var decoder = new Dictionary<char, byte>(byteValues.Count);
        for (int index = 0; index < byteValues.Count; index++)
        {
            decoder[(char)unicodeValues[index]] = (byte)byteValues[index];
        }

        return decoder;
    }

    private sealed record WhisperModelConfig(
        int DecoderStartTokenId,
        int EndOfTranscriptTokenId,
        IReadOnlyList<int> ForcedDecoderIds,
        IReadOnlyList<int> SuppressTokens,
        IReadOnlyList<int> BeginSuppressTokens);
}
