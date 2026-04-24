using System.Collections.ObjectModel;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Transcripts;
using BabelStudio.Contracts.Pipeline;
using BabelStudio.Domain;
using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Speakers;
using BabelStudio.Domain.Translation;
using BabelStudio.Media.Playback;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace BabelStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private static readonly Color UnassignedSpeakerColor = Color.FromArgb(255, 128, 128, 128);
    private static readonly Color[] SpeakerPalette =
    [
        Color.FromArgb(255, 219, 96, 69),
        Color.FromArgb(255, 66, 145, 241),
        Color.FromArgb(255, 101, 182, 94),
        Color.FromArgb(255, 227, 180, 74)
    ];

    private Guid? currentTranscriptRevisionId;
    private Guid? currentTranslationRevisionId;
    private string? currentTranslationTargetLanguageCode;
    private WaveformSummary? currentWaveformSummary;
    private string currentSubtitleText = string.Empty;
    private string defaultSourceLanguageCode = "en";
    private string defaultTargetLanguageCode = "es";
    private bool hasPlaybackBackend;
    private bool isBusy;
    private string? lastErrorReport;
    private string mediaPath = "No media selected.";
    private string playbackAssessmentWarning = string.Empty;
    private string playbackBackendLabel = "No media loaded.";
    private double playbackDurationSeconds;
    private string playbackPositionText = "00:00 / 00:00";
    private double playbackPositionSeconds;
    private bool playbackLoaded;
    private string playbackRuntimeWarning = string.Empty;
    private string playbackWarning = string.Empty;
    private string persistedTranscriptLanguageCode = string.Empty;
    private string projectNameDraft = "New Project";
    private string projectRootPath = "No project loaded.";
    private bool enableSpeakerDiarizationOnImport = true;
    private string selectedModelTier = "balanced";
    private string? selectedTranslationTargetLanguageCode;
    private string? selectedTranscriptLanguageCode;
    private bool showTranslatedSubtitles;
    private string sourceStatus = "No project loaded.";
    private string statusMessage = "Open media to create a project, or open an existing .babelstudio folder.";
    private string translationTargetStatus = "Choose a translation target after loading a project.";
    private string translationRefreshStatus = "Not translated";
    private string translationRevisionLabel = "No translation loaded.";
    private string translationStageStatus = "Not run";
    private string diarizationStageStatus = "Not run";
    private string transcriptRevisionLabel = "No transcript loaded.";
    private string vadStageStatus = "Not run";
    private string asrStageStatus = "Not run";
    private string windowLayoutSummary = "Window layout not captured yet.";
    private bool commercialSafeMode = true;

    public MainWindowViewModel()
    {
        Segments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanSaveTranscript));
            OnPropertyChanged(nameof(CanSaveTranslation));
            OnPropertyChanged(nameof(CanShowTranslatedSubtitles));
        };
    }

    public ObservableCollection<TranscriptSegmentItem> Segments { get; } = [];

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public ObservableCollection<TranslationTargetLanguageOption> SupportedTranslationTargets { get; } = [];

    public ObservableCollection<SpeakerItem> Speakers { get; } = [];

    public IReadOnlyList<TranscriptLanguageChoice> TranscriptLanguageOptions { get; } =
    [
        new("en", "English"),
        new("es", "Spanish")
    ];

    public IReadOnlyList<TranscriptLanguageChoice> TranslationLanguageOptions { get; } =
    [
        new("en", "English"),
        new("es", "Spanish"),
        new("fr", "French"),
        new("de", "German"),
        new("it", "Italian"),
        new("pt", "Portuguese"),
        new("ja", "Japanese")
    ];

    public IReadOnlyList<ModelTierChoice> ModelTierOptions { get; } =
    [
        new("fast", "Fast"),
        new("balanced", "Balanced"),
        new("quality", "Quality")
    ];

    public IReadOnlyList<PlaybackSpeedChoice> PlaybackSpeedOptions { get; } =
    [
        new(0.5, "0.5x"),
        new(1.0, "1x"),
        new(1.25, "1.25x"),
        new(1.5, "1.5x")
    ];

    public string ProjectNameDraft
    {
        get => projectNameDraft;
        set => SetProperty(ref projectNameDraft, value);
    }

    public string ProjectRootPath
    {
        get => projectRootPath;
        private set => SetProperty(ref projectRootPath, value);
    }

    public bool EnableSpeakerDiarizationOnImport
    {
        get => enableSpeakerDiarizationOnImport;
        set => SetProperty(ref enableSpeakerDiarizationOnImport, value);
    }

    public string MediaPath
    {
        get => mediaPath;
        private set => SetProperty(ref mediaPath, value);
    }

    public string SourceStatus
    {
        get => sourceStatus;
        private set => SetProperty(ref sourceStatus, value);
    }

    public string VadStageStatus
    {
        get => vadStageStatus;
        private set => SetProperty(ref vadStageStatus, value);
    }

    public string AsrStageStatus
    {
        get => asrStageStatus;
        private set => SetProperty(ref asrStageStatus, value);
    }

    public string TranslationStageStatus
    {
        get => translationStageStatus;
        private set => SetProperty(ref translationStageStatus, value);
    }

    public string DiarizationStageStatus
    {
        get => diarizationStageStatus;
        private set => SetProperty(ref diarizationStageStatus, value);
    }

    public string TranscriptRevisionLabel
    {
        get => transcriptRevisionLabel;
        private set => SetProperty(ref transcriptRevisionLabel, value);
    }

    public string TranslationRevisionLabel
    {
        get => translationRevisionLabel;
        private set => SetProperty(ref translationRevisionLabel, value);
    }

    public string TranslationRefreshStatus
    {
        get => translationRefreshStatus;
        private set => SetProperty(ref translationRefreshStatus, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        set => SetProperty(ref statusMessage, value);
    }

    public bool IsBusy
    {
        get => isBusy;
        set
        {
            if (SetProperty(ref isBusy, value))
            {
                OnPropertyChanged(nameof(CanSaveTranscript));
                OnPropertyChanged(nameof(CanTranslate));
                OnPropertyChanged(nameof(CanSaveTranslation));
            }
        }
    }

    public string? SelectedTranscriptLanguageCode
    {
        get => selectedTranscriptLanguageCode;
        set
        {
            string? normalized = NormalizeLanguageCode(value);
            if (SetProperty(ref selectedTranscriptLanguageCode, normalized))
            {
                OnPropertyChanged(nameof(CanTranslate));
                OnPropertyChanged(nameof(HasTranscriptLanguageChangePending));
                OnPropertyChanged(nameof(TranscriptLanguageSummary));
                OnPropertyChanged(nameof(TranslateButtonText));
                OnPropertyChanged(nameof(TranslationEditorLabel));
                OnPropertyChanged(nameof(SelectedTranslationTargetLanguageCode));
            }
        }
    }

    public string? SelectedTranslationTargetLanguageCode
    {
        get => selectedTranslationTargetLanguageCode;
        set
        {
            string? normalized = NormalizeLanguageCode(value);
            if (SetProperty(ref selectedTranslationTargetLanguageCode, normalized))
            {
                UpdateTranslationTargetStatus();
                UpdateActiveSegment(PlaybackPositionSeconds);
                OnPropertyChanged(nameof(CanTranslate));
                OnPropertyChanged(nameof(TranslateButtonText));
                OnPropertyChanged(nameof(TranslationEditorLabel));
            }
        }
    }

    public string DefaultSourceLanguageCode
    {
        get => defaultSourceLanguageCode;
        set => SetProperty(ref defaultSourceLanguageCode, NormalizeLanguageCode(value) ?? "en");
    }

    public string DefaultTargetLanguageCode
    {
        get => defaultTargetLanguageCode;
        set => SetProperty(ref defaultTargetLanguageCode, NormalizeLanguageCode(value) ?? "es");
    }

    public string SelectedModelTier
    {
        get => selectedModelTier;
        set => SetProperty(ref selectedModelTier, string.IsNullOrWhiteSpace(value) ? "balanced" : value.Trim().ToLowerInvariant());
    }

    public bool CommercialSafeMode
    {
        get => commercialSafeMode;
        set => SetProperty(ref commercialSafeMode, value);
    }

    public string TranslationTargetStatus
    {
        get => translationTargetStatus;
        private set
        {
            if (SetProperty(ref translationTargetStatus, value))
            {
                OnPropertyChanged(nameof(TranscriptLanguageSummary));
            }
        }
    }

    public string WindowLayoutSummary
    {
        get => windowLayoutSummary;
        private set => SetProperty(ref windowLayoutSummary, value);
    }

    public string PlaybackBackendLabel
    {
        get => playbackBackendLabel;
        private set => SetProperty(ref playbackBackendLabel, value);
    }

    public string PlaybackWarning
    {
        get => playbackWarning;
        private set => SetProperty(ref playbackWarning, value);
    }

    public string PlaybackPositionText
    {
        get => playbackPositionText;
        private set => SetProperty(ref playbackPositionText, value);
    }

    public double PlaybackPositionSeconds
    {
        get => playbackPositionSeconds;
        private set => SetProperty(ref playbackPositionSeconds, value);
    }

    public double PlaybackDurationSeconds
    {
        get => playbackDurationSeconds;
        private set => SetProperty(ref playbackDurationSeconds, value);
    }

    public string CurrentSubtitleText
    {
        get => currentSubtitleText;
        private set => SetProperty(ref currentSubtitleText, value);
    }

    public bool ShowTranslatedSubtitles
    {
        get => showTranslatedSubtitles;
        set
        {
            bool normalized = value && CanShowTranslatedSubtitles;
            if (SetProperty(ref showTranslatedSubtitles, normalized))
            {
                UpdateActiveSegment(PlaybackPositionSeconds);
            }
        }
    }

    public bool HasPlaybackBackend
    {
        get => hasPlaybackBackend;
        private set => SetProperty(ref hasPlaybackBackend, value);
    }

    public WaveformSummary? CurrentWaveformSummary
    {
        get => currentWaveformSummary;
        private set => SetProperty(ref currentWaveformSummary, value);
    }

    public Guid? CurrentTranscriptRevisionId => currentTranscriptRevisionId;

    public bool CanSaveTranscript => !IsBusy && currentTranscriptRevisionId is not null && Segments.Count > 0;

    public bool CanTranslate =>
        !IsBusy &&
        currentTranscriptRevisionId is not null &&
        RequestedTranslationTargetLanguageCode is not null &&
        SelectedTranslationTargetOption?.IsAvailable == true;

    public bool CanSaveTranslation =>
        !IsBusy &&
        currentTranslationRevisionId is not null &&
        Segments.Count > 0;

    public bool CanCopyError => !string.IsNullOrWhiteSpace(lastErrorReport);

    public bool CanPlayMedia => HasPlaybackBackend && playbackLoaded;

    public bool CanShowTranslatedSubtitles =>
        currentTranslationRevisionId is not null &&
        Segments.Any(segment => !string.IsNullOrWhiteSpace(segment.TranslationText));

    public string? LastErrorReport => lastErrorReport;

    public string TranslateButtonText => currentTranslationRevisionId is null
        ? BuildTranslateButtonText(hasExistingTranslation: false)
        : BuildTranslateButtonText(HasLoadedTranslationForRequestedDirection);

    public string TranslationEditorLabel => $"{GetLanguageDisplayName(LoadedOrRequestedTranslationTargetLanguageCode)} Draft";

    public bool HasTranscriptLanguageChangePending =>
        !string.Equals(persistedTranscriptLanguageCode, SelectedTranscriptLanguageCode, StringComparison.Ordinal);

    public string TranscriptLanguageSummary =>
        SelectedTranscriptLanguageCode switch
        {
            "en" => $"English transcript. {TranslationTargetStatus}",
            "es" => $"Spanish transcript. {TranslationTargetStatus}",
            _ => "Choose whether the transcript is English or Spanish."
        };

    public void ApplySettings(StudioSettings settings)
    {
        DefaultSourceLanguageCode = settings.DefaultSourceLanguage ?? "en";
        DefaultTargetLanguageCode = settings.DefaultTargetLanguage ?? "es";
        SelectedModelTier = settings.ModelTierPreference;
        CommercialSafeMode = settings.CommercialSafeMode;
        WindowLayoutSummary = settings.WindowLayout.Width is null || settings.WindowLayout.Height is null
            ? "Window layout will be captured after the first save."
            : $"{settings.WindowLayout.Width:0}x{settings.WindowLayout.Height:0}" +
              (settings.WindowLayout.IsMaximized ? " (maximized)" : string.Empty);

        RecentProjects.Clear();
        foreach (RecentProjectEntry entry in settings.RecentProjects.OrderByDescending(entry => entry.LastOpenedAtUtc))
        {
            RecentProjects.Add(new RecentProjectItem(entry.ProjectName, entry.ProjectPath, entry.LastOpenedAtUtc));
        }

        if (currentTranscriptRevisionId is null && string.IsNullOrWhiteSpace(SelectedTranscriptLanguageCode))
        {
            SelectedTranscriptLanguageCode = DefaultSourceLanguageCode;
        }
    }

    public StudioSettings CreateSettings(WindowLayoutSettings windowLayout)
    {
        RecentProjectEntry[] recentProjects = RecentProjects
            .OrderByDescending(entry => entry.LastOpenedAtUtc)
            .Select(entry => new RecentProjectEntry(entry.ProjectName, entry.ProjectPath, entry.LastOpenedAtUtc))
            .ToArray();

        return new StudioSettings(
            DefaultSourceLanguageCode,
            DefaultTargetLanguageCode,
            SelectedModelTier,
            CommercialSafeMode,
            windowLayout,
            recentProjects);
    }

    public void SetProjectNameFromMedia(string mediaPath)
    {
        string fileName = Path.GetFileNameWithoutExtension(mediaPath);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            ProjectNameDraft = fileName.Trim();
        }
    }

    public void ApplyProjectState(TranscriptProjectState state, string loadedProjectRootPath)
    {
        currentTranscriptRevisionId = state.CurrentTranscriptRevision?.Id;
        currentTranslationRevisionId = state.CurrentTranslationRevision?.Id;
        persistedTranscriptLanguageCode = NormalizeLanguageCode(state.TranscriptLanguage) ?? string.Empty;
        currentTranslationTargetLanguageCode = NormalizeLanguageCode(state.CurrentTranslationRevision?.TargetLanguage);
        SelectedTranscriptLanguageCode = string.IsNullOrWhiteSpace(persistedTranscriptLanguageCode)
            ? DefaultSourceLanguageCode
            : persistedTranscriptLanguageCode;
        ApplySupportedTranslationTargets(state.SupportedTargetLanguages);
        SelectedTranslationTargetLanguageCode = ResolveSelectedTranslationTargetLanguageCode(state);

        ProjectRootPath = loadedProjectRootPath;
        MediaPath = state.ProjectState.SourceReference?.OriginalPath ?? state.ProjectState.MediaAsset?.SourceFilePath ?? "Source media reference missing.";
        CurrentWaveformSummary = state.WaveformSummary;
        PlaybackDurationSeconds = state.WaveformSummary?.DurationSeconds ?? state.ProjectState.MediaAsset?.DurationSeconds ?? 0d;

        string statusSummary = state.ProjectState.SourceStatus.ToString();
        if (!string.IsNullOrWhiteSpace(state.ProjectState.SourceStatusMessage))
        {
            statusSummary = $"{statusSummary}: {state.ProjectState.SourceStatusMessage}";
        }

        SourceStatus = statusSummary;
        VadStageStatus = BuildStageStatus(state.StageRuns, "vad");
        AsrStageStatus = BuildStageStatus(state.StageRuns, "asr");
        DiarizationStageStatus = BuildStageStatus(state.StageRuns, "diarization");
        TranslationStageStatus = BuildStageStatus(state.StageRuns, "translation");
        TranscriptRevisionLabel = state.CurrentTranscriptRevision is null
            ? "No transcript loaded."
            : $"Revision {state.CurrentTranscriptRevision.RevisionNumber} with {state.TranscriptSegments.Count} segment(s).";
        TranslationRevisionLabel = state.CurrentTranslationRevision is null
            ? BuildMissingTranslationLabel()
            : BuildTranslationRevisionLabel(state);
        TranslationRefreshStatus = state.CurrentTranslationRevision is null
            ? "Not translated"
            : state.IsTranslationStale
                ? $"Needs Refresh ({state.StaleTranslatedSegmentIndices.Count} stale segment(s))"
                : "Current";

        Dictionary<int, string> translatedTextByIndex = state.TranslatedSegments
            .ToDictionary(segment => segment.SegmentIndex, segment => segment.Text);
        IReadOnlySet<int> staleTranslatedSegmentIndices = state.StaleTranslatedSegmentIndices;
        Dictionary<Guid, SpeakerVisual> speakerVisuals = BuildSpeakerVisuals(state.Speakers);
        SpeakerChoiceItem[] speakerChoices = state.Speakers
            .Select(speaker => new SpeakerChoiceItem(speaker.Id, speaker.DisplayName))
            .ToArray();
        HashSet<Guid> referenceClipSpeakerIds = ResolveReferenceClipSpeakerIds(state.ProjectState.Artifacts);
        Dictionary<Guid, SpeakerTurnItem[]> turnsBySpeakerId = state.SpeakerTurns
            .GroupBy(turn => turn.SpeakerId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(turn => turn.StartSeconds)
                    .Select(turn => new SpeakerTurnItem(
                        turn.Id,
                        turn.SpeakerId,
                        turn.StartSeconds,
                        turn.EndSeconds,
                        turn.Confidence,
                        turn.HasOverlap))
                    .ToArray());

        Speakers.Clear();
        foreach (ProjectSpeaker speaker in state.Speakers.OrderBy(speaker => speaker.CreatedAtUtc))
        {
            SpeakerVisual visual = speakerVisuals[speaker.Id];
            SpeakerChoiceItem[] mergeTargets = speakerChoices
                .Where(choice => choice.SpeakerId != speaker.Id)
                .ToArray();
            turnsBySpeakerId.TryGetValue(speaker.Id, out SpeakerTurnItem[]? speakerTurns);
            Speakers.Add(new SpeakerItem(
                speaker.Id,
                speaker.DisplayName,
                visual.Color,
                referenceClipSpeakerIds.Contains(speaker.Id),
                mergeTargets,
                speakerTurns ?? []));
        }

        Segments.Clear();
        foreach (TranscriptSegmentItem item in state.TranscriptSegments
                     .OrderBy(segment => segment.SegmentIndex)
                     .Select(segment => new TranscriptSegmentItem(
                        segment,
                        segment.SpeakerId,
                        ResolveSpeakerLabel(segment.SpeakerId, speakerVisuals),
                        ResolveSpeakerColor(segment.SpeakerId, speakerVisuals),
                        ResolveSpeakerBrush(segment.SpeakerId, speakerVisuals),
                        speakerChoices,
                        TranslationEditorLabel,
                        translatedTextByIndex.TryGetValue(segment.SegmentIndex, out string? translatedText)
                            ? translatedText
                            : string.Empty,
                        staleTranslatedSegmentIndices.Contains(segment.SegmentIndex))))
        {
            Segments.Add(item);
        }

        if (!CanShowTranslatedSubtitles)
        {
            ShowTranslatedSubtitles = false;
        }

        ProjectNameDraft = state.ProjectState.Project.Name;
        StageRunRecord? failedStageRun = GetLatestFailedStageRun(state.StageRuns);
        if (failedStageRun is not null)
        {
            ReportStageFailure(failedStageRun);
        }
        else
        {
            StatusMessage = BuildLoadedStatusMessage(state);
            ClearError();
        }

        UpdateActiveSegment(PlaybackPositionSeconds);

        OnPropertyChanged(nameof(CanSaveTranscript));
        OnPropertyChanged(nameof(CanTranslate));
        OnPropertyChanged(nameof(CanSaveTranslation));
        OnPropertyChanged(nameof(TranslateButtonText));
        OnPropertyChanged(nameof(TranslationEditorLabel));
        OnPropertyChanged(nameof(HasTranscriptLanguageChangePending));
        OnPropertyChanged(nameof(TranscriptLanguageSummary));
        OnPropertyChanged(nameof(CanShowTranslatedSubtitles));
    }

    public void ApplyPlaybackAssessment(PlaybackCapabilityAssessment? assessment, bool hasBackend)
    {
        HasPlaybackBackend = hasBackend;
        OnPropertyChanged(nameof(CanPlayMedia));

        if (assessment is null)
        {
            playbackAssessmentWarning = string.Empty;
            PlaybackBackendLabel = "No media loaded.";
            ApplyPlaybackSnapshot(PlaybackSnapshot.Empty);
            return;
        }

        playbackAssessmentWarning = assessment.WarningMessage ?? string.Empty;
        if (!hasBackend)
        {
            playbackLoaded = false;
        }

        PlaybackBackendLabel = assessment.PreferredBackend switch
        {
            PlaybackBackendKind.MediaFoundation => "Media Foundation",
            PlaybackBackendKind.FfmpegFallback => "FFmpeg fallback required",
            PlaybackBackendKind.LibMpvFallback => "libmpv fallback required",
            _ => assessment.PreferredBackend.ToString()
        };
        UpdatePlaybackWarning();
    }

    public void ApplyPlaybackSnapshot(PlaybackSnapshot snapshot)
    {
        playbackLoaded = snapshot.IsLoaded;
        playbackRuntimeWarning = snapshot.WarningMessage ?? string.Empty;

        if (!snapshot.IsLoaded)
        {
            PlaybackPositionSeconds = 0d;
            PlaybackDurationSeconds = 0d;
            PlaybackPositionText = "00:00 / 00:00";
            ClearActivePlaybackState();
            UpdatePlaybackWarning();
            OnPropertyChanged(nameof(CanPlayMedia));
            return;
        }

        PlaybackPositionSeconds = snapshot.Position.TotalSeconds;
        PlaybackDurationSeconds = snapshot.Duration > TimeSpan.Zero ? snapshot.Duration.TotalSeconds : 0d;
        PlaybackPositionText = $"{FormatClock(snapshot.Position)} / {FormatClock(snapshot.Duration)}";
        UpdatePlaybackWarning();
        OnPropertyChanged(nameof(CanPlayMedia));
        UpdateActiveSegment(PlaybackPositionSeconds);
    }

    public void UpdateActiveSegment(double playbackPositionInSeconds)
    {
        TranscriptSegmentItem? activeSegment = null;
        foreach (TranscriptSegmentItem segment in Segments)
        {
            bool isActive = playbackPositionInSeconds >= segment.StartSeconds &&
                            playbackPositionInSeconds <= segment.EndSeconds;
            segment.IsActive = isActive;
            if (isActive)
            {
                activeSegment = segment;
            }
        }

        CurrentSubtitleText = activeSegment?.GetSubtitleText(ShowTranslatedSubtitles) ?? string.Empty;
    }

    public void BeginOperation(string busyMessage)
    {
        ClearError();
        IsBusy = true;
        StatusMessage = busyMessage;
    }

    public void ReportException(Exception exception, string context)
    {
        SetErrorReport(exception.Message, BuildExceptionErrorReport(exception, context));
    }

    public void ClearError()
    {
        if (string.IsNullOrWhiteSpace(lastErrorReport))
        {
            return;
        }

        lastErrorReport = null;
        OnPropertyChanged(nameof(CanCopyError));
        OnPropertyChanged(nameof(LastErrorReport));
    }

    public SaveTranscriptEditsRequest? CreateSaveRequest()
    {
        if (currentTranscriptRevisionId is null || Segments.Count == 0)
        {
            return null;
        }

        return new SaveTranscriptEditsRequest(
            currentTranscriptRevisionId.Value,
            Segments.Select(segment => new EditedTranscriptSegment(segment.SegmentId, segment.Text, segment.SelectedSpeakerId)).ToArray());
    }

    public SaveTranslationEditsRequest? CreateSaveTranslationRequest()
    {
        if (currentTranslationRevisionId is null || Segments.Count == 0)
        {
            return null;
        }

        return new SaveTranslationEditsRequest(
            currentTranslationRevisionId.Value,
            currentTranslationTargetLanguageCode ?? throw new InvalidOperationException("The current translation target language is not available."),
            Segments.Select(segment => new EditedTranslatedSegment(segment.SegmentIndex, segment.TranslationText)).ToArray());
    }

    public SplitTranscriptSegmentRequest? CreateSplitRequest(TranscriptSegmentItem segment, double splitSeconds)
    {
        return currentTranscriptRevisionId is null
            ? null
            : new SplitTranscriptSegmentRequest(currentTranscriptRevisionId.Value, segment.SegmentId, splitSeconds);
    }

    public TrimTranscriptSegmentRequest? CreateTrimRequest(TranscriptSegmentItem segment)
    {
        return currentTranscriptRevisionId is null
            ? null
            : new TrimTranscriptSegmentRequest(currentTranscriptRevisionId.Value, segment.SegmentId, segment.StartSeconds, segment.EndSeconds);
    }

    public DeleteTranscriptSegmentRequest? CreateDeleteRequest(TranscriptSegmentItem segment)
    {
        return currentTranscriptRevisionId is null
            ? null
            : new DeleteTranscriptSegmentRequest(currentTranscriptRevisionId.Value, segment.SegmentId);
    }

    public MergeTranscriptSegmentsRequest? CreateMergeRequest(IReadOnlyList<TranscriptSegmentItem> selectedSegments)
    {
        if (currentTranscriptRevisionId is null || selectedSegments.Count != 2)
        {
            return null;
        }

        TranscriptSegmentItem[] ordered = selectedSegments.OrderBy(segment => segment.SegmentIndex).ToArray();
        return new MergeTranscriptSegmentsRequest(currentTranscriptRevisionId.Value, ordered[0].SegmentId, ordered[1].SegmentId);
    }

    public RenameSpeakerRequest? CreateRenameSpeakerRequest(SpeakerItem speaker)
    {
        if (!speaker.HasRenamePending || string.IsNullOrWhiteSpace(speaker.DisplayName))
        {
            return null;
        }

        return new RenameSpeakerRequest(speaker.SpeakerId, speaker.DisplayName);
    }

    public MergeSpeakersRequest? CreateMergeSpeakersRequest(SpeakerItem speaker)
    {
        if (speaker.MergeTargetSpeakerId is not Guid targetSpeakerId || targetSpeakerId == speaker.SpeakerId)
        {
            return null;
        }

        return new MergeSpeakersRequest(speaker.SpeakerId, targetSpeakerId);
    }

    public AssignSpeakerToSegmentRequest? CreateAssignSpeakerRequest(TranscriptSegmentItem segment)
    {
        if (currentTranscriptRevisionId is null || segment.SelectedSpeakerId is not Guid speakerId)
        {
            return null;
        }

        return new AssignSpeakerToSegmentRequest(currentTranscriptRevisionId.Value, segment.SegmentId, speakerId);
    }

    public SplitSpeakerTurnRequest? CreateSplitSpeakerTurnRequest(SpeakerTurnItem turn, double splitSeconds)
    {
        return !double.IsFinite(splitSeconds)
            ? null
            : new SplitSpeakerTurnRequest(turn.TurnId, splitSeconds);
    }

    public ExtractReferenceClipRequest CreateExtractReferenceClipRequest(SpeakerItem speaker) =>
        new(speaker.SpeakerId);

    public string? GetRequestedTranslationTargetLanguageCode() => RequestedTranslationTargetLanguageCode;

    private static string BuildStageStatus(IReadOnlyList<StageRunRecord> stageRuns, string stageName)
    {
        StageRunRecord? latest = stageRuns
            .Where(stageRun => string.Equals(stageRun.StageName, stageName, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(stageRun => stageRun.StartedAtUtc)
            .FirstOrDefault();

        if (latest is null)
        {
            return "Not run";
        }

        string providerSuffix = latest.RuntimeInfo is null
            ? string.Empty
            : $" via {latest.RuntimeInfo.SelectedProvider}";

        return latest.Status switch
        {
            StageRunStatus.Completed => $"Completed{providerSuffix}",
            StageRunStatus.Failed => $"Failed{providerSuffix}: {latest.FailureReason}",
            _ => "Running"
        };
    }

    private string BuildTranslateButtonText(bool hasExistingTranslation)
    {
        string displayName = GetLanguageDisplayName(RequestedTranslationTargetLanguageCode);
        return hasExistingTranslation
            ? $"Re-translate to {displayName}"
            : $"Translate to {displayName}";
    }

    private static string BuildLoadedStatusMessage(TranscriptProjectState state)
    {
        if (state.CurrentTranscriptRevision is null)
        {
            return "Project loaded. No transcript revision is stored yet.";
        }

        if (state.CurrentTranslationRevision is not null)
        {
            string translationDisplayName = GetLanguageDisplayName(state.CurrentTranslationRevision.TargetLanguage).ToLowerInvariant();
            return state.IsTranslationStale
                ? $"Project loaded. Existing {translationDisplayName} translation needs refresh after transcript changes."
                : "Project loaded. Edit transcript timing, transcript text, or translation text and save to create a new revision.";
        }

        TranslationTargetLanguageOption? selectedTarget = ResolveSelectedTranslationTarget(state);
        if (selectedTarget is null)
        {
            return NormalizeLanguageCode(state.TranscriptLanguage) switch
            {
                "en" or "es" => "Project loaded. Install a translation model to enable translation review.",
                _ => "Project loaded. Choose whether the transcript is English or Spanish to continue."
            };
        }

        string targetDisplayName = GetLanguageDisplayName(selectedTarget.LanguageCode);
        return NormalizeLanguageCode(state.TranscriptLanguage) switch
        {
            "en" or "es" when selectedTarget.IsAvailable =>
                $"Project loaded. Translate to {targetDisplayName} to create the first draft translation.",
            "en" or "es" => $"Project loaded. {selectedTarget.Detail}",
            _ => "Project loaded. Choose whether the transcript is English or Spanish to continue."
        };
    }

    private void ReportStageFailure(StageRunRecord stageRun)
    {
        string stageDisplayName = stageRun.StageName.ToUpperInvariant();
        string failureReason = string.IsNullOrWhiteSpace(stageRun.FailureReason)
            ? "Unknown failure."
            : stageRun.FailureReason;

        SetErrorReport(
            $"{stageDisplayName} failed: {failureReason}",
            BuildStageFailureReport(stageRun));
    }

    private void SetErrorReport(string status, string report)
    {
        lastErrorReport = report;
        StatusMessage = status;
        OnPropertyChanged(nameof(CanCopyError));
        OnPropertyChanged(nameof(LastErrorReport));
    }

    private static StageRunRecord? GetLatestFailedStageRun(IReadOnlyList<StageRunRecord> stageRuns)
    {
        return stageRuns
            .GroupBy(stageRun => stageRun.StageName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(stageRun => stageRun.StartedAtUtc)
                .First())
            .Where(stageRun => stageRun.Status is StageRunStatus.Failed)
            .OrderByDescending(stageRun => stageRun.StartedAtUtc)
            .FirstOrDefault();
    }

    private void UpdatePlaybackWarning()
    {
        string[] warnings =
        [
            playbackAssessmentWarning,
            playbackRuntimeWarning
        ];

        PlaybackWarning = string.Join(
            ' ',
            warnings
                .Where(warning => !string.IsNullOrWhiteSpace(warning))
                .Select(warning => warning.Trim())
                .Distinct(StringComparer.Ordinal));
    }

    private void ClearActivePlaybackState()
    {
        foreach (TranscriptSegmentItem segment in Segments)
        {
            segment.IsActive = false;
        }

        CurrentSubtitleText = string.Empty;
    }

    private string BuildExceptionErrorReport(Exception exception, string context)
    {
        return string.Join(Environment.NewLine,
        [
            "BabelStudio.App error report",
            $"Time (UTC): {DateTimeOffset.UtcNow:O}",
            $"Context: {context}",
            $"Project root: {ProjectRootPath}",
            $"Media path: {MediaPath}",
            "Exception:",
            exception.ToString()
        ]);
    }

    private string BuildStageFailureReport(StageRunRecord stageRun)
    {
        return string.Join(Environment.NewLine,
        [
            "BabelStudio.App stage failure report",
            $"Time (UTC): {DateTimeOffset.UtcNow:O}",
            $"Project root: {ProjectRootPath}",
            $"Media path: {MediaPath}",
            $"Stage: {stageRun.StageName}",
            $"Status: {stageRun.Status}",
            $"Started (UTC): {stageRun.StartedAtUtc:O}",
            $"Completed (UTC): {stageRun.CompletedAtUtc:O}",
            $"Failure reason: {stageRun.FailureReason ?? "Unknown failure."}",
            $"Requested provider: {stageRun.RuntimeInfo?.RequestedProvider ?? "unknown"}",
            $"Selected provider: {stageRun.RuntimeInfo?.SelectedProvider ?? "unknown"}",
            $"Model id: {stageRun.RuntimeInfo?.ModelId ?? "n/a"}",
            $"Model alias: {stageRun.RuntimeInfo?.ModelAlias ?? "n/a"}",
            $"Model variant: {stageRun.RuntimeInfo?.ModelVariant ?? "n/a"}",
            $"Bootstrap detail: {stageRun.RuntimeInfo?.BootstrapDetail ?? "n/a"}"
        ]);
    }

    private static string? NormalizeLanguageCode(string? languageCode) =>
        string.IsNullOrWhiteSpace(languageCode)
            ? null
            : languageCode.Trim().ToLowerInvariant();

    private static Dictionary<Guid, SpeakerVisual> BuildSpeakerVisuals(IReadOnlyList<ProjectSpeaker> speakers)
    {
        return speakers
            .OrderBy(speaker => speaker.CreatedAtUtc)
            .Select((speaker, index) =>
            {
                Color color = SpeakerPalette[index % SpeakerPalette.Length];
                Brush? brush = WinUiBrushFactory.TryCreateSolidColorBrush(color);
                return new
                {
                    speaker.Id,
                    Visual = new SpeakerVisual(speaker.DisplayName, color, brush)
                };
            })
            .ToDictionary(entry => entry.Id, entry => entry.Visual);
    }

    private static HashSet<Guid> ResolveReferenceClipSpeakerIds(IReadOnlyList<ProjectArtifact> artifacts)
    {
        var speakerIds = new HashSet<Guid>();
        foreach (ProjectArtifact artifact in artifacts.Where(artifact => artifact.Kind == ArtifactKind.ReferenceClip))
        {
            if (!string.IsNullOrWhiteSpace(artifact.Provenance) &&
                artifact.Provenance.StartsWith("speaker-reference:", StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParse(artifact.Provenance["speaker-reference:".Length..], out Guid provenanceSpeakerId))
            {
                speakerIds.Add(provenanceSpeakerId);
                continue;
            }

            string[] parts = artifact.RelativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
            foreach (string part in parts)
            {
                if (Guid.TryParse(part, out Guid pathSpeakerId))
                {
                    speakerIds.Add(pathSpeakerId);
                    break;
                }
            }
        }

        return speakerIds;
    }

    private static string ResolveSpeakerLabel(Guid? speakerId, IReadOnlyDictionary<Guid, SpeakerVisual> speakerVisuals)
    {
        return speakerId is Guid resolvedSpeakerId && speakerVisuals.TryGetValue(resolvedSpeakerId, out SpeakerVisual? visual)
            ? visual.DisplayName
            : "Unassigned";
    }

    private static Color ResolveSpeakerColor(Guid? speakerId, IReadOnlyDictionary<Guid, SpeakerVisual> speakerVisuals)
    {
        return speakerId is Guid resolvedSpeakerId && speakerVisuals.TryGetValue(resolvedSpeakerId, out SpeakerVisual? visual)
            ? visual.Color
            : UnassignedSpeakerColor;
    }

    private static Brush? ResolveSpeakerBrush(Guid? speakerId, IReadOnlyDictionary<Guid, SpeakerVisual> speakerVisuals)
    {
        return speakerId is Guid resolvedSpeakerId && speakerVisuals.TryGetValue(resolvedSpeakerId, out SpeakerVisual? visual)
            ? visual.Brush
            : WinUiBrushFactory.TryCreateSolidColorBrush(UnassignedSpeakerColor);
    }

    private void ApplySupportedTranslationTargets(IReadOnlyList<TranslationTargetLanguageOption> options)
    {
        SupportedTranslationTargets.Clear();
        foreach (TranslationTargetLanguageOption option in options)
        {
            SupportedTranslationTargets.Add(option);
        }

        UpdateTranslationTargetStatus();
    }

    private string? ResolveSelectedTranslationTargetLanguageCode(TranscriptProjectState state)
    {
        string? selectedTargetLanguageCode = NormalizeLanguageCode(state.SelectedTranslationTargetLanguage);
        if (selectedTargetLanguageCode is not null &&
            SupportedTranslationTargets.Any(option => string.Equals(option.LanguageCode, selectedTargetLanguageCode, StringComparison.Ordinal)))
        {
            return selectedTargetLanguageCode;
        }

        string? defaultTargetLanguageCode = NormalizeLanguageCode(DefaultTargetLanguageCode);
        if (defaultTargetLanguageCode is not null &&
            SupportedTranslationTargets.Any(option => string.Equals(option.LanguageCode, defaultTargetLanguageCode, StringComparison.Ordinal)))
        {
            return defaultTargetLanguageCode;
        }

        return SupportedTranslationTargets.FirstOrDefault(option => option.IsAvailable)?.LanguageCode
               ?? SupportedTranslationTargets.FirstOrDefault()?.LanguageCode;
    }

    private void UpdateTranslationTargetStatus()
    {
        TranslationTargetLanguageOption? selectedTarget = SelectedTranslationTargetOption;
        if (selectedTarget is null)
        {
            TranslationTargetStatus = SupportedTranslationTargets.Count == 0
                ? "No translation routes are currently available."
                : "Choose a translation target.";
            return;
        }

        TranslationTargetStatus = selectedTarget.IsAvailable
            ? selectedTarget.Detail
            : $"Unavailable - {selectedTarget.Detail}";
    }

    private static TranslationTargetLanguageOption? ResolveSelectedTranslationTarget(TranscriptProjectState state)
    {
        string? selectedTargetLanguageCode = NormalizeLanguageCode(state.SelectedTranslationTargetLanguage);
        if (selectedTargetLanguageCode is null)
        {
            return state.SupportedTargetLanguages.FirstOrDefault(option => option.IsAvailable)
                   ?? state.SupportedTargetLanguages.FirstOrDefault();
        }

        return state.SupportedTargetLanguages.FirstOrDefault(option =>
                   string.Equals(option.LanguageCode, selectedTargetLanguageCode, StringComparison.Ordinal))
               ?? state.SupportedTargetLanguages.FirstOrDefault(option => option.IsAvailable)
               ?? state.SupportedTargetLanguages.FirstOrDefault();
    }

    private static string BuildTranslationRevisionLabel(TranscriptProjectState state)
    {
        TranslationRevision revision = state.CurrentTranslationRevision
            ?? throw new InvalidOperationException("Translation revision is required.");
        string providerSuffix = string.IsNullOrWhiteSpace(revision.TranslationProvider)
            ? string.Empty
            : $" via {revision.TranslationProvider}";
        return $"{GetLanguageDisplayName(revision.TargetLanguage)} revision {revision.RevisionNumber} with {state.TranslatedSegments.Count} segment(s){providerSuffix}.";
    }

    private string BuildMissingTranslationLabel()
    {
        string? targetLanguageCode = LoadedOrRequestedTranslationTargetLanguageCode;
        return targetLanguageCode is null
            ? "No translation loaded."
            : $"No {GetLanguageDisplayName(targetLanguageCode)} translation loaded.";
    }

    private bool HasLoadedTranslationForRequestedDirection =>
        currentTranslationRevisionId is not null &&
        string.Equals(currentTranslationTargetLanguageCode, RequestedTranslationTargetLanguageCode, StringComparison.Ordinal);

    private string? LoadedOrRequestedTranslationTargetLanguageCode =>
        currentTranslationTargetLanguageCode ?? RequestedTranslationTargetLanguageCode;

    private string? RequestedTranslationTargetLanguageCode =>
        NormalizeLanguageCode(selectedTranslationTargetLanguageCode)
        ?? SupportedTranslationTargets.FirstOrDefault(option => option.IsAvailable)?.LanguageCode
        ?? SupportedTranslationTargets.FirstOrDefault()?.LanguageCode;

    private TranslationTargetLanguageOption? SelectedTranslationTargetOption =>
        SupportedTranslationTargets.FirstOrDefault(option =>
            string.Equals(option.LanguageCode, RequestedTranslationTargetLanguageCode, StringComparison.Ordinal));

    private static string GetLanguageDisplayName(string? languageCode) =>
        NormalizeLanguageCode(languageCode) switch
        {
            "en" => "English",
            "es" => "Spanish",
            "fr" => "French",
            "de" => "German",
            "it" => "Italian",
            "pt" => "Portuguese",
            "ja" => "Japanese",
            _ => "Translation"
        };

    private static string FormatClock(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "00:00";
        }

        return value.TotalHours >= 1d
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");
    }

    private sealed record SpeakerVisual(
        string DisplayName,
        Color Color,
        Brush? Brush);
}
