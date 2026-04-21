using System.Text.Json;
using Microsoft.ML.Tokenizers;

namespace BabelStudio.Inference.Onnx.OpusMt;

internal sealed class OpusTokenizerDecoder
{
    private readonly SentencePieceTokenizer sourceTokenizer;
    private readonly SentencePieceTokenizer targetTokenizer;
    private readonly IReadOnlyDictionary<string, int> modelIdByPiece;
    private readonly IReadOnlyDictionary<int, string> pieceByModelId;
    private readonly IReadOnlyDictionary<int, string> sourcePieceByTokenizerId;
    private readonly IReadOnlyDictionary<string, int> targetTokenizerIdByPiece;

    private OpusTokenizerDecoder(
        SentencePieceTokenizer sourceTokenizer,
        SentencePieceTokenizer targetTokenizer,
        IReadOnlyDictionary<string, int> modelIdByPiece,
        IReadOnlyDictionary<int, string> pieceByModelId,
        IReadOnlyDictionary<int, string> sourcePieceByTokenizerId,
        IReadOnlyDictionary<string, int> targetTokenizerIdByPiece,
        int decoderStartTokenId,
        int endOfSentenceTokenId,
        int padTokenId,
        int maxGenerationLength)
    {
        this.sourceTokenizer = sourceTokenizer;
        this.targetTokenizer = targetTokenizer;
        this.modelIdByPiece = modelIdByPiece;
        this.pieceByModelId = pieceByModelId;
        this.sourcePieceByTokenizerId = sourcePieceByTokenizerId;
        this.targetTokenizerIdByPiece = targetTokenizerIdByPiece;
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
        string vocabPath = Path.Combine(modelRootPath, "vocab.json");
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

        if (!File.Exists(vocabPath))
        {
            throw new FileNotFoundException("The Opus vocabulary mapping was not found.", vocabPath);
        }

        OpusTokenizerConfig config = LoadConfig(configPath, generationConfigPath);
        using FileStream sourceStream = File.OpenRead(sourceTokenizerPath);
        using FileStream targetStream = File.OpenRead(targetTokenizerPath);
        IReadOnlyDictionary<string, int> modelIdByPiece = LoadVocabulary(vocabPath);

        SentencePieceTokenizer sourceTokenizer = SentencePieceTokenizer.Create(
            sourceStream,
            addBeginningOfSentence: false,
            addEndOfSentence: true);
        SentencePieceTokenizer targetTokenizer = SentencePieceTokenizer.Create(
            targetStream,
            addBeginningOfSentence: false,
            addEndOfSentence: false);
        IReadOnlyDictionary<int, string> pieceByModelId = modelIdByPiece
            .ToDictionary(pair => pair.Value, pair => pair.Key);
        IReadOnlyDictionary<int, string> sourcePieceByTokenizerId = sourceTokenizer.Vocabulary
            .ToDictionary(pair => pair.Value, pair => pair.Key);

        return new OpusTokenizerDecoder(
            sourceTokenizer,
            targetTokenizer,
            modelIdByPiece,
            pieceByModelId,
            sourcePieceByTokenizerId,
            targetTokenizer.Vocabulary,
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
            .Select(MapSourceTokenizerIdToModelId)
            .Select(static tokenId => (long)tokenId)
            .ToArray();
    }

    public string DecodeTargetText(IEnumerable<long> tokenIds)
    {
        int[] targetTokenizerIds = tokenIds
            .Where(tokenId => tokenId != DecoderStartTokenId &&
                              tokenId != EndOfSentenceTokenId &&
                              tokenId != PadTokenId)
            .Select(MapModelIdToTargetTokenizerId)
            .ToArray();
        return targetTokenizer.Decode(targetTokenizerIds).Trim();
    }

    private int MapSourceTokenizerIdToModelId(int tokenizerId)
    {
        if (!sourcePieceByTokenizerId.TryGetValue(tokenizerId, out string? piece))
        {
            throw new InvalidOperationException($"Source tokenizer piece id '{tokenizerId}' did not resolve to a Marian token piece.");
        }

        if (modelIdByPiece.TryGetValue(piece, out int modelId))
        {
            return modelId;
        }

        if (modelIdByPiece.TryGetValue(sourceTokenizer.UnknownToken, out int unknownId))
        {
            return unknownId;
        }

        throw new InvalidOperationException($"Marian vocabulary did not define a model id for token piece '{piece}'.");
    }

    private int MapModelIdToTargetTokenizerId(long modelTokenId)
    {
        int checkedModelTokenId = checked((int)modelTokenId);
        if (!pieceByModelId.TryGetValue(checkedModelTokenId, out string? piece))
        {
            throw new InvalidOperationException($"Marian vocabulary did not define token piece for model id '{checkedModelTokenId}'.");
        }

        if (targetTokenizerIdByPiece.TryGetValue(piece, out int tokenizerId))
        {
            return tokenizerId;
        }

        if (targetTokenizerIdByPiece.TryGetValue(targetTokenizer.UnknownToken, out int unknownId))
        {
            return unknownId;
        }

        throw new InvalidOperationException($"Target tokenizer did not define an id for Marian token piece '{piece}'.");
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

    private static IReadOnlyDictionary<string, int> LoadVocabulary(string vocabPath)
    {
        Dictionary<string, int>? vocabulary = JsonSerializer.Deserialize<Dictionary<string, int>>(
            File.ReadAllText(vocabPath));
        if (vocabulary is null || vocabulary.Count == 0)
        {
            throw new InvalidOperationException($"The Opus Marian vocabulary at '{vocabPath}' was empty or invalid.");
        }

        return vocabulary;
    }

    private sealed record OpusTokenizerConfig(
        int DecoderStartTokenId,
        int EndOfSentenceTokenId,
        int PadTokenId,
        int MaxGenerationLength);
}
