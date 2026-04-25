using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.Inference.Onnx.Kokoro;

public sealed class KokoroVoiceCatalog : IVoiceCatalog
{
    private readonly string modelRootPath;
    private readonly IReadOnlyList<VoiceCatalogEntry> voices;

    private KokoroVoiceCatalog(string modelRootPath, IReadOnlyList<VoiceCatalogEntry> voices)
    {
        this.modelRootPath = modelRootPath;
        this.voices = voices;
    }

    public static KokoroVoiceCatalog Load(string modelRootPath)
    {
        string voicesDirectory = Path.Combine(modelRootPath, "voices");
        if (!Directory.Exists(voicesDirectory))
        {
            return new KokoroVoiceCatalog(modelRootPath, []);
        }

        var entries = new List<VoiceCatalogEntry>();
        foreach (string binPath in Directory.EnumerateFiles(voicesDirectory, "*.bin", SearchOption.TopDirectoryOnly))
        {
            string voiceId = Path.GetFileNameWithoutExtension(binPath);
            if (TryParseVoiceEntry(voiceId, out VoiceCatalogEntry? entry))
            {
                entries.Add(entry);
            }
        }

        return new KokoroVoiceCatalog(modelRootPath, [.. entries.OrderBy(static v => v.VoiceId)]);
    }

    public IReadOnlyList<VoiceCatalogEntry> GetVoices(string? languageCode = null) =>
        languageCode is null
            ? voices
            : voices.Where(v => v.LanguageCode == languageCode).ToList();

    public bool TryGetVoice(string voiceId, [NotNullWhen(true)] out VoiceCatalogEntry? entry)
    {
        entry = voices.FirstOrDefault(v => v.VoiceId == voiceId);
        return entry is not null;
    }

    internal string? GetBinPath(string voiceId)
    {
        string path = Path.Combine(modelRootPath, "voices", $"{voiceId}.bin");
        return File.Exists(path) ? path : null;
    }

    private static bool TryParseVoiceEntry(string voiceId, [NotNullWhen(true)] out VoiceCatalogEntry? entry)
    {
        // Naming convention: {locale}{gender}_{name}  e.g. af_heart, bm_george
        entry = null;
        if (voiceId.Length < 3 || voiceId[2] != '_')
        {
            return false;
        }

        string languageCode = voiceId[0] switch
        {
            'a' => "en-us",
            'b' => "en-gb",
            'e' => "es",
            'f' => "fr",
            'h' => "hi",
            'i' => "it",
            'j' => "ja",
            'k' => "ko",
            'p' => "pt",
            'r' => "ru",
            'z' => "zh",
            _ => "unknown"
        };

        string gender = voiceId[1] switch
        {
            'f' => "female",
            'm' => "male",
            _ => "unknown"
        };

        string namePart = voiceId[3..];
        string displayName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(namePart.Replace('_', ' '));

        entry = new VoiceCatalogEntry(voiceId, languageCode, gender, displayName);
        return true;
    }
}
