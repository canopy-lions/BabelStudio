using BabelStudio.Domain.Transcript;

namespace BabelStudio.App.ViewModels;

public sealed class TranscriptSegmentItem : ObservableObject
{
    private string text;
    private string translationLabel;
    private string translationText;

    public TranscriptSegmentItem(TranscriptSegment segment, string translationLabel, string translationText)
    {
        SegmentId = segment.Id;
        SegmentIndex = segment.SegmentIndex;
        StartSeconds = segment.StartSeconds;
        EndSeconds = segment.EndSeconds;
        text = segment.Text;
        this.translationLabel = translationLabel;
        this.translationText = translationText;
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

    public string TranslationLabel
    {
        get => translationLabel;
        set => SetProperty(ref translationLabel, value);
    }

    public string TranslationText
    {
        get => translationText;
        set => SetProperty(ref translationText, value);
    }
}
