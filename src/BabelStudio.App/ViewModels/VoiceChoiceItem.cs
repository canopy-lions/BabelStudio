namespace BabelStudio.App.ViewModels;

public sealed record VoiceChoiceItem(
    string VoiceId,
    string DisplayName,
    string LanguageCode)
{
    public string DisplayLabel => $"{DisplayName} ({LanguageCode})";
}
