using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BabelStudio.App.ViewModels;

public sealed class SpeakerItem : ObservableObject
{
    private string displayName;
    private Guid? mergeTargetSpeakerId;
    private string? selectedVoiceId;

    public SpeakerItem(
        Guid speakerId,
        string displayName,
        Color accentColor,
        bool hasReferenceClip,
        IReadOnlyList<SpeakerChoiceItem> mergeTargets,
        IReadOnlyList<SpeakerTurnItem> turns,
        IReadOnlyList<VoiceChoiceItem> voiceOptions,
        string? selectedVoiceId,
        string voiceAssignmentStatus,
        string voiceWarning,
        bool hasStaleTts)
    {
        SpeakerId = speakerId;
        OriginalDisplayName = displayName;
        this.displayName = displayName;
        this.selectedVoiceId = selectedVoiceId;
        AccentColor = accentColor;
        AccentBrush = WinUiBrushFactory.TryCreateSolidColorBrush(accentColor);
        HasReferenceClip = hasReferenceClip;
        VoiceAssignmentStatus = voiceAssignmentStatus;
        VoiceWarning = voiceWarning;
        HasStaleTts = hasStaleTts;
        foreach (SpeakerChoiceItem target in mergeTargets)
        {
            MergeTargets.Add(target);
        }

        foreach (SpeakerTurnItem turn in turns)
        {
            Turns.Add(turn);
        }

        foreach (VoiceChoiceItem voice in voiceOptions)
        {
            VoiceOptions.Add(voice);
        }
    }

    public Guid SpeakerId { get; }

    public string OriginalDisplayName { get; }

    public Color AccentColor { get; }

    public Brush? AccentBrush { get; }

    public bool HasReferenceClip { get; }

    public ObservableCollection<SpeakerChoiceItem> MergeTargets { get; } = [];

    public ObservableCollection<SpeakerTurnItem> Turns { get; } = [];

    public ObservableCollection<VoiceChoiceItem> VoiceOptions { get; } = [];

    public string DisplayName
    {
        get => displayName;
        set
        {
            if (SetProperty(ref displayName, value))
            {
                OnPropertyChanged(nameof(HasRenamePending));
            }
        }
    }

    public Guid? MergeTargetSpeakerId
    {
        get => mergeTargetSpeakerId;
        set => SetProperty(ref mergeTargetSpeakerId, value);
    }

    public string? SelectedVoiceId
    {
        get => selectedVoiceId;
        set => SetProperty(ref selectedVoiceId, value);
    }

    public string VoiceAssignmentStatus { get; }

    public string VoiceWarning { get; }

    public bool HasStaleTts { get; }

    public bool HasRenamePending => !string.Equals(
        OriginalDisplayName,
        displayName,
        StringComparison.Ordinal);

    public string TurnSummary => Turns.Count == 0
        ? "No diarized turns"
        : $"{Turns.Count} diarized turn(s)";

    public string ReferenceClipStatus => HasReferenceClip ? "Reference clip ready" : "No reference clip";
}
