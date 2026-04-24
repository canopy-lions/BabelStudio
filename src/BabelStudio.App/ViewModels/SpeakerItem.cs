using System.Collections.ObjectModel;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BabelStudio.App.ViewModels;

public sealed class SpeakerItem : ObservableObject
{
    private string displayName;
    private Guid? mergeTargetSpeakerId;

    public SpeakerItem(
        Guid speakerId,
        string displayName,
        Color accentColor,
        bool hasReferenceClip,
        IReadOnlyList<SpeakerChoiceItem> mergeTargets,
        IReadOnlyList<SpeakerTurnItem> turns)
    {
        SpeakerId = speakerId;
        OriginalDisplayName = displayName;
        this.displayName = displayName;
        AccentColor = accentColor;
        AccentBrush = WinUiBrushFactory.TryCreateSolidColorBrush(accentColor);
        HasReferenceClip = hasReferenceClip;
        foreach (SpeakerChoiceItem target in mergeTargets)
        {
            MergeTargets.Add(target);
        }

        foreach (SpeakerTurnItem turn in turns)
        {
            Turns.Add(turn);
        }
    }

    public Guid SpeakerId { get; }

    public string OriginalDisplayName { get; }

    public Color AccentColor { get; }

    public Brush? AccentBrush { get; }

    public bool HasReferenceClip { get; }

    public ObservableCollection<SpeakerChoiceItem> MergeTargets { get; } = [];

    public ObservableCollection<SpeakerTurnItem> Turns { get; } = [];

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

    public bool HasRenamePending => !string.Equals(
        OriginalDisplayName,
        displayName,
        StringComparison.Ordinal);

    public string TurnSummary => Turns.Count == 0
        ? "No diarized turns"
        : $"{Turns.Count} diarized turn(s)";

    public string ReferenceClipStatus => HasReferenceClip ? "Reference clip ready" : "No reference clip";
}
