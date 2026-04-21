using BabelStudio.Inference.Onnx.Whisper;

namespace BabelStudio.Inference.Tests;

public sealed class WhisperTokenizerDecoderTests
{
    [Fact]
    public void BuildTranscriptionPrompt_DoesNotReuseForcedEnglishToken()
    {
        string modelRootPath = ResolveModelRootPath("whisper-tiny-onnx");
        var tokenizer = WhisperTokenizerDecoder.Load(modelRootPath);

        IReadOnlyList<int> prompt = tokenizer.BuildTranscriptionPrompt(languageTokenId: 50262);

        Assert.Equal([ 50258, 50262, 50359, 50363 ], prompt);
        Assert.DoesNotContain(50259, prompt);
    }

    private static string ResolveModelRootPath(string modelDirectoryName) =>
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "..",
            "models",
            modelDirectoryName));
}
