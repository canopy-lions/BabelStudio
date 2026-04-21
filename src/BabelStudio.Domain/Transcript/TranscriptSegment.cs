namespace BabelStudio.Domain.Transcript;

public sealed record TranscriptSegment(
    Guid Id,
    Guid TranscriptRevisionId,
    int SegmentIndex,
    double StartSeconds,
    double EndSeconds,
    string Text)
{
    public static TranscriptSegment Create(
        Guid transcriptRevisionId,
        int segmentIndex,
        double startSeconds,
        double endSeconds,
        string text)
    {
        if (transcriptRevisionId == Guid.Empty)
        {
            throw new ArgumentException("Transcript revision id is required.", nameof(transcriptRevisionId));
        }

        if (segmentIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), "Segment index cannot be negative.");
        }

        if (startSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startSeconds), "Segment start must be non-negative.");
        }

        if (endSeconds < startSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(endSeconds), "Segment end must be greater than or equal to the start.");
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Segment text is required.", nameof(text));
        }

        return new TranscriptSegment(
            Guid.NewGuid(),
            transcriptRevisionId,
            segmentIndex,
            startSeconds,
            endSeconds,
            text.Trim());
    }
}
