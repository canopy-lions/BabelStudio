using System.Diagnostics.CodeAnalysis;
using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.TestDoubles;

public sealed class FakeVoiceCatalog : IVoiceCatalog
{
    private readonly IReadOnlyList<VoiceCatalogEntry> voices;

    public FakeVoiceCatalog(IReadOnlyList<VoiceCatalogEntry>? voices = null)
    {
        this.voices = voices ?? DefaultVoices();
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

    private static IReadOnlyList<VoiceCatalogEntry> DefaultVoices() =>
    [
        new("af_heart", "en-us", "female", "Heart (American English)"),
        new("am_adam",  "en-us", "male",   "Adam (American English)"),
        new("bf_alice", "en-gb", "female", "Alice (British English)")
    ];
}
