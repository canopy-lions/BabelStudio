using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace BabelStudio.Inference.Onnx.OpusMt;

internal sealed class OpusTokenizerDecoder
{
    private readonly Tokenizer sourceTokenizer;
    private readonly Tokenizer targetTokenizer;

    private OpusTokenizerDecoder(
        Tokenizer sourceTokenizer,
        Tokenizer targetTokenizer,
        int decoderStartTokenId,
        int endOfSentenceTokenId,
        int padTokenId,
        int maxGenerationLength)
    {
        this.sourceTokenizer = sourceTokenizer;
        this.targetTokenizer = targetTokenizer;
        DecoderStartTokenId = decoderStartTokenId;
        EndOfSentenceTokenId = endOfSentenceTokenId;
        PadTokenId = padTokenId;
        MaxGenerationLength = maxGenerationLength;
    }

    public int DecoderStartTokenId { get; }

    public int EndOfSentenceTokenId { get; }

    public int PadTokenId { get; }

    public int MaxGenerationLength { get; }

    public static OpusTokenizerDecoder Load(string modelRootPath)
    {
        string sourceTokenizerPath = Path.Combine(modelRootPath, "source.spm");
        string targetTokenizerPath = Path.Combine(modelRootPath, "target.spm");
        string configPath = Path.Combine(modelRootPath, "config.json");
        string generationConfigPath = Path.Combine(modelRootPath, "generation_config.json");

        if (!File.Exists(sourceTokenizerPath))
        {
            throw new FileNotFoundException("The Opus source tokenizer was not found.", sourceTokenizerPath);
        }

        if (!File.Exists(targetTokenizerPath))
        {
            throw new FileNotFoundException("The Opus target tokenizer was not found.", targetTokenizerPath);
        }

        OpusTokenizerConfig config = LoadConfig(configPath, generationConfigPath);
        using FileStream sourceStream = File.OpenRead(sourceTokenizerPath);
        using FileStream targetStream = File.OpenRead(targetTokenizerPath);

        Tokenizer sourceTokenizer = SentencePieceTokenizer.Create(
            sourceStream,
            addBeginningOfSentence: false,
            addEndOfSentence: true);
        Tokenizer targetTokenizer = SentencePieceTokenizer.Create(
            targetStream,
            addBeginningOfSentence: false,
            addEndOfSentence: false);

        return new OpusTokenizerDecoder(
            sourceTokenizer,
            targetTokenizer,
            config.DecoderStartTokenId,
            config.EndOfSentenceTokenId,
            config.PadTokenId,
            config.MaxGenerationLength);
    }

    public long[] EncodeSourceText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return sourceTokenizer
            .EncodeToIds(text.Trim())
            .Select(static tokenId => (long)tokenId)
            .ToArray();
    }

    public string DecodeTargetText(IEnumerable<long> tokenIds)
    {
        int[] filteredTokenIds = tokenIds
            .Where(tokenId => tokenId != DecoderStartTokenId &&
                              tokenId != EndOfSentenceTokenId &&
                              tokenId != PadTokenId)
            .Select(static tokenId => checked((int)tokenId))
            .ToArray();
        return targetTokenizer.Decode(filteredTokenIds).Trim();
    }

    private static OpusTokenizerConfig LoadConfig(string configPath, string generationConfigPath)
    {
        int decoderStartTokenId = 65000;
        int endOfSentenceTokenId = 0;
        int padTokenId = 65000;
        int maxGenerationLength = 256;

        if (File.Exists(configPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(configPath));
            JsonElement root = document.RootElement;
            decoderStartTokenId = ReadInt32(root, "decoder_start_token_id", decoderStartTokenId);
            endOfSentenceTokenId = ReadInt32(root, "eos_token_id", endOfSentenceTokenId);
            padTokenId = ReadInt32(root, "pad_token_id", padTokenId);
            maxGenerationLength = ReadInt32(root, "max_position_embeddings", maxGenerationLength);
        }

        if (File.Exists(generationConfigPath))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(generationConfigPath));
            JsonElement root = document.RootElement;
            maxGenerationLength = ReadInt32(root, "max_length", maxGenerationLength);
        }

        return new OpusTokenizerConfig(
            decoderStartTokenId,
            endOfSentenceTokenId,
            padTokenId,
            Math.Max(32, maxGenerationLength));
    }

    private static int ReadInt32(JsonElement root, string propertyName, int defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement element))
        {
            return defaultValue;
        }

        return element.ValueKind is JsonValueKind.Number && element.TryGetInt32(out int value)
            ? value
            : defaultValue;
    }

    private sealed record OpusTokenizerConfig(
        int DecoderStartTokenId,
        int EndOfSentenceTokenId,
        int PadTokenId,
        int MaxGenerationLength);
}
