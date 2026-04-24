namespace BabelStudio.App.ViewModels;

public sealed class SpeakerTurnItem
{
    public SpeakerTurnItem(
        Guid turnId,
        Guid speakerId,
        double startSeconds,
        double endSeconds,
        double? confidence,
        bool hasOverlap)
    {
        TurnId = turnId;
        SpeakerId = speakerId;
        StartSeconds = startSeconds;
        EndSeconds = endSeconds;
        Confidence = confidence;
        HasOverlap = hasOverlap;
    }

    public Guid TurnId { get; }

    public Guid SpeakerId { get; }

    public double StartSeconds { get; }

    public double EndSeconds { get; }

    public double? Confidence { get; }

    public bool HasOverlap { get; }

    public string DisplayTimeRange => $"{StartSeconds,6:F2}s - {EndSeconds,6:F2}s";

    public string ConfidenceLabel => Confidence is null
        ? string.Empty
        : $"Confidence {Confidence.Value:P0}";

    public string OverlapLabel => HasOverlap ? "Overlap detected" : string.Empty;
}
