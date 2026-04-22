using System.Collections.ObjectModel;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Transcripts;
using BabelStudio.Domain;
using BabelStudio.Media.Playback;

namespace BabelStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
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
    private string selectedModelTier = "balanced";
    private string? selectedTranscriptLanguageCode;
    private string sourceStatus = "No project loaded.";
    private string statusMessage = "Open media to create a project, or open an existing .babelstudio folder.";
    private string translationRefreshStatus = "Not translated";
    private string translationRevisionLabel = "No translation loaded.";
    private string translationStageStatus = "Not run";
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
        };
    }

    public ObservableCollection<TranscriptSegmentItem> Segments { get; } = [];

    public ObservableCollection<RecentProjectItem> RecentProjects { get; } = [];

    public IReadOnlyList<TranscriptLanguageChoice> TranscriptLanguageOptions { get; } =
    [
        new("en", "English"),
        new("es", "Spanish")
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

    public bool CanSaveTranscript => !IsBusy && currentTranscriptRevisionId is not null && Segments.Count > 0;

    public bool CanTranslate =>
        !IsBusy &&
        currentTranscriptRevisionId is not null &&
        RequestedTranslationTargetLanguageCode is not null;

    public bool CanSaveTranslation =>
        !IsBusy &&
        currentTranslationRevisionId is not null &&
        Segments.Count > 0;

    public bool CanCopyError => !string.IsNullOrWhiteSpace(lastErrorReport);

    public bool CanPlayMedia => HasPlaybackBackend && playbackLoaded;

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
            "en" => "English transcript. Spanish translation is enabled.",
            "es" => "Spanish transcript. English translation is enabled.",
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
        currentTranslationTargetLanguageCode = NormalizeLanguageCode(state.CurrentTranslationRevision?.TargetLanguage) ??
                                               GetTranslationTargetLanguageCode(persistedTranscriptLanguageCode);
        SelectedTranscriptLanguageCode = string.IsNullOrWhiteSpace(persistedTranscriptLanguageCode)
            ? DefaultSourceLanguageCode
            : persistedTranscriptLanguageCode;

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
        TranslationStageStatus = BuildStageStatus(state.StageRuns, "translation");
        TranscriptRevisionLabel = state.CurrentTranscriptRevision is null
            ? "No transcript loaded."
            : $"Revision {state.CurrentTranscriptRevision.RevisionNumber} with {state.TranscriptSegments.Count} segment(s).";
        TranslationRevisionLabel = state.CurrentTranslationRevision is null
            ? BuildMissingTranslationLabel()
            : $"{GetLanguageDisplayName(state.CurrentTranslationRevision.TargetLanguage)} revision {state.CurrentTranslationRevision.RevisionNumber} with {state.TranslatedSegments.Count} segment(s).";
        TranslationRefreshStatus = state.CurrentTranslationRevision is null
            ? "Not translated"
            : state.IsTranslationStale
                ? "Needs Refresh"
                : "Current";

        Dictionary<int, string> translatedTextByIndex = state.TranslatedSegments
            .ToDictionary(segment => segment.SegmentIndex, segment => segment.Text);

        Segments.Clear();
        foreach (TranscriptSegmentItem item in state.TranscriptSegments
                     .OrderBy(segment => segment.SegmentIndex)
                     .Select(segment => new TranscriptSegmentItem(
                         segment,
                         TranslationEditorLabel,
                         translatedTextByIndex.TryGetValue(segment.SegmentIndex, out string? translatedText)
                             ? translatedText
                             : string.Empty)))
        {
            Segments.Add(item);
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
    }

    public void ApplyPlaybackAssessment(PlaybackCapabilityAssessment? assessment, bool hasBackend)
    {
        HasPlaybackBackend = hasBackend;
        OnPropertyChanged(nameof(CanPlayMedia));

        if (assessment is null)
        {
            playbackAssessmentWarning = string.Empty;
            playbackRuntimeWarning = string.Empty;
            playbackLoaded = false;
            PlaybackBackendLabel = "No media loaded.";
            UpdatePlaybackWarning();
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

        CurrentSubtitleText = activeSegment?.Text ?? string.Empty;
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
            Segments.Select(segment => new EditedTranscriptSegment(segment.SegmentId, segment.Text)).ToArray());
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

    public MergeTranscriptSegmentsRequest? CreateMergeRequest(IReadOnlyList<TranscriptSegmentItem> selectedSegments)
    {
        if (currentTranscriptRevisionId is null || selectedSegments.Count != 2)
        {
            return null;
        }

        TranscriptSegmentItem[] ordered = selectedSegments.OrderBy(segment => segment.SegmentIndex).ToArray();
        return new MergeTranscriptSegmentsRequest(currentTranscriptRevisionId.Value, ordered[0].SegmentId, ordered[1].SegmentId);
    }

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

        string? targetLanguageCode = GetTranslationTargetLanguageCode(state.TranscriptLanguage);
        string targetDisplayName = GetLanguageDisplayName(targetLanguageCode);
        return NormalizeLanguageCode(state.TranscriptLanguage) switch
        {
            "en" => $"Project loaded. Translate to {targetDisplayName} to create the first draft translation.",
            "es" => $"Project loaded. Translate to {targetDisplayName} to create the first draft translation.",
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
        GetTranslationTargetLanguageCode(SelectedTranscriptLanguageCode ?? persistedTranscriptLanguageCode);

    private static string? GetTranslationTargetLanguageCode(string? transcriptLanguageCode) =>
        NormalizeLanguageCode(transcriptLanguageCode) switch
        {
            "en" => "es",
            "es" => "en",
            _ => null
        };

    private static string GetLanguageDisplayName(string? languageCode) =>
        NormalizeLanguageCode(languageCode) switch
        {
            "en" => "English",
            "es" => "Spanish",
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
}
