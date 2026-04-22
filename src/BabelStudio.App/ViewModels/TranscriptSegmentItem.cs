using BabelStudio.Domain.Transcript;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace BabelStudio.App.ViewModels;

public sealed class TranscriptSegmentItem : ObservableObject
{
    private static readonly Brush ActiveBackgroundBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 35, 51, 69));
    private static readonly Brush InactiveBackgroundBrush = new SolidColorBrush(Colors.Transparent);
    private double endSeconds;
    private bool isActive;
    private double startSeconds;
    private string text;
    private string translationLabel;
    private string translationText;

    public TranscriptSegmentItem(TranscriptSegment segment, string translationLabel, string translationText)
    {
        SegmentId = segment.Id;
        SegmentIndex = segment.SegmentIndex;
        startSeconds = segment.StartSeconds;
        endSeconds = segment.EndSeconds;
        text = segment.Text;
        this.translationLabel = translationLabel;
        this.translationText = translationText;
    }

    public Guid SegmentId { get; }

    public int SegmentIndex { get; }

    public double StartSeconds
    {
        get => startSeconds;
        set
        {
            if (SetProperty(ref startSeconds, value))
            {
                OnPropertyChanged(nameof(DisplayTimeRange));
            }
        }
    }

    public double EndSeconds
    {
        get => endSeconds;
        set
        {
            if (SetProperty(ref endSeconds, value))
            {
                OnPropertyChanged(nameof(DisplayTimeRange));
            }
        }
    }

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

    public bool IsActive
    {
        get => isActive;
        set
        {
            if (SetProperty(ref isActive, value))
            {
                OnPropertyChanged(nameof(BackgroundBrush));
            }
        }
    }

    public Brush BackgroundBrush => IsActive ? ActiveBackgroundBrush : InactiveBackgroundBrush;
}
