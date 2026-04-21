using BabelStudio.Domain.Transcript;

namespace BabelStudio.App.ViewModels;

public sealed class TranscriptSegmentItem : ObservableObject
{
    private string text;

    public TranscriptSegmentItem(TranscriptSegment segment)
    {
        SegmentId = segment.Id;
        SegmentIndex = segment.SegmentIndex;
        StartSeconds = segment.StartSeconds;
        EndSeconds = segment.EndSeconds;
        text = segment.Text;
    }

    public Guid SegmentId { get; }

    public int SegmentIndex { get; }

    public double StartSeconds { get; }

    public double EndSeconds { get; }

    public string DisplayTimeRange => $"{StartSeconds,6:F2}s - {EndSeconds,6:F2}s";

    public string Text
    {
        get => text;
        set => SetProperty(ref text, value);
    }
}
