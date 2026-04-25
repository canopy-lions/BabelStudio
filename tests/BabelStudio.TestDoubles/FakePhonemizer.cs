using BabelStudio.Contracts.Pipeline;

namespace BabelStudio.TestDoubles;

public sealed class FakePhonemizer : IGraphemeToPhoneme
{
    private readonly string fixedPhonemes;

    public FakePhonemizer(string fixedPhonemes = "h@l@U")
    {
        this.fixedPhonemes = fixedPhonemes;
    }

    public string Phonemize(string text, string languageCode) => fixedPhonemes;
}
