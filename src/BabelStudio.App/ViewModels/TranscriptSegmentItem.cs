using BabelStudio.Domain.Transcript;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using Windows.UI;

namespace BabelStudio.App.ViewModels;

public sealed class TranscriptSegmentItem : ObservableObject
{
    private static readonly Brush? ActiveBackgroundBrush = WinUiBrushFactory.TryCreateSolidColorBrush(Color.FromArgb(255, 35, 51, 69));
    private static readonly Brush? InactiveBackgroundBrush = WinUiBrushFactory.TryCreateSolidColorBrush(Color.FromArgb(0, 0, 0, 0));
    private double endSeconds;
    private bool isActive;
    private bool isTranslationStale;
    private Color speakerColor;
    private Guid? selectedSpeakerId;
    private Brush? speakerBrush;
    private double startSeconds;
    private string speakerLabel;
    private string text;
    private string ttsStatusLabel;
    private string ttsWarningLabel;
    private string translationLabel;
    private string translationText;

    public TranscriptSegmentItem(
        TranscriptSegment segment,
        Guid? selectedSpeakerId,
        string speakerLabel,
        Color speakerColor,
        Brush? speakerBrush,
        IReadOnlyList<SpeakerChoiceItem> speakerOptions,
        string translationLabel,
        string translationText,
        bool isTranslationStale,
        string ttsStatusLabel,
        string ttsWarningLabel,
        string? ttsArtifactRelativePath)
    {
        SegmentId = segment.Id;
        SegmentIndex = segment.SegmentIndex;
        startSeconds = segment.StartSeconds;
        endSeconds = segment.EndSeconds;
        this.selectedSpeakerId = selectedSpeakerId;
        this.speakerLabel = speakerLabel;
        this.speakerColor = speakerColor;
        this.speakerBrush = speakerBrush;
        text = segment.Text;
        this.translationLabel = translationLabel;
        this.translationText = translationText;
        this.isTranslationStale = isTranslationStale;
        this.ttsStatusLabel = ttsStatusLabel;
        this.ttsWarningLabel = ttsWarningLabel;
        TtsArtifactRelativePath = ttsArtifactRelativePath;

        foreach (SpeakerChoiceItem option in speakerOptions)
        {
            SpeakerOptions.Add(option);
        }
    }

    public Guid SegmentId { get; }

    public int SegmentIndex { get; }

    public ObservableCollection<SpeakerChoiceItem> SpeakerOptions { get; } = [];

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

    public Guid? SelectedSpeakerId
    {
        get => selectedSpeakerId;
        set => SetProperty(ref selectedSpeakerId, value);
    }

    public string SpeakerLabel
    {
        get => speakerLabel;
        set => SetProperty(ref speakerLabel, value);
    }

    public Brush? SpeakerBrush
    {
        get => speakerBrush;
        set => SetProperty(ref speakerBrush, value);
    }

    public Color SpeakerColor
    {
        get => speakerColor;
        set => SetProperty(ref speakerColor, value);
    }

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

    public bool IsTranslationStale
    {
        get => isTranslationStale;
        set
        {
            if (SetProperty(ref isTranslationStale, value))
            {
                OnPropertyChanged(nameof(TranslationStatusLabel));
            }
        }
    }

    public string TranslationStatusLabel => IsTranslationStale ? "Stale translation" : string.Empty;

    public string TtsStatusLabel
    {
        get => ttsStatusLabel;
        set => SetProperty(ref ttsStatusLabel, value);
    }

    public string TtsWarningLabel
    {
        get => ttsWarningLabel;
        set
        {
            if (SetProperty(ref ttsWarningLabel, value))
            {
                OnPropertyChanged(nameof(HasTtsDurationWarning));
            }
        }
    }

    public string? TtsArtifactRelativePath { get; }

    public bool CanAuditionTts => !string.IsNullOrWhiteSpace(TtsArtifactRelativePath);

    public bool HasTtsDurationWarning => !string.IsNullOrWhiteSpace(TtsWarningLabel);

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

    public Brush? BackgroundBrush => IsActive ? ActiveBackgroundBrush : InactiveBackgroundBrush;

    public string GetSubtitleText(bool preferTranslation) =>
        preferTranslation && !string.IsNullOrWhiteSpace(TranslationText)
            ? TranslationText
            : Text;
}
