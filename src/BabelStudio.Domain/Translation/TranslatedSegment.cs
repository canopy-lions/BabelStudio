namespace BabelStudio.Domain.Translation;

public sealed record TranslatedSegment(
    Guid Id,
    Guid TranslationRevisionId,
    int SegmentIndex,
    double StartSeconds,
    double EndSeconds,
    string Text,
    string? SourceSegmentHash)
{
    public static TranslatedSegment Create(
        Guid translationRevisionId,
        int segmentIndex,
        double startSeconds,
        double endSeconds,
        string text,
        string? sourceSegmentHash = null)
    {
        if (translationRevisionId == Guid.Empty)
        {
            throw new ArgumentException("Translation revision id is required.", nameof(translationRevisionId));
        }

        if (segmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), "Segment index cannot be negative.");
        }

        if (!double.IsFinite(startSeconds) || startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Segment start must be finite and non-negative.");
        }

        if (!double.IsFinite(endSeconds) || endSeconds < startSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Segment end must be finite and greater than or equal to the start.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Segment text is required.", nameof(text));
        }

        return new TranslatedSegment(
            Guid.NewGuid(),
            translationRevisionId,
            segmentIndex,
            startSeconds,
            endSeconds,
            text.Trim(),
            NormalizeOptional(sourceSegmentHash));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
