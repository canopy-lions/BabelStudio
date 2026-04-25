using System.Diagnostics.CodeAnalysis;

namespace BabelStudio.Contracts.Pipeline;

public interface IVoiceCatalog
{
    IReadOnlyList<VoiceCatalogEntry> GetVoices(string? languageCode = null);
    bool TryGetVoice(string voiceId, [NotNullWhen(true)] out VoiceCatalogEntry? entry);
}
