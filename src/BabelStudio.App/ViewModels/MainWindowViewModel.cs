using System.Collections.ObjectModel;
using BabelStudio.Application.Transcripts;
using BabelStudio.Domain;

namespace BabelStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private Guid? currentTranscriptRevisionId;
    private Guid? currentTranslationRevisionId;
    private string? lastErrorReport;
    private string? persistedTranscriptLanguageCode;
    private string? selectedTranscriptLanguageCode;
    private string projectNameDraft = "New Project";
    private string projectRootPath = "No project loaded.";
    private string mediaPath = "No media selected.";
    private string sourceStatus = "No project loaded.";
    private string vadStageStatus = "Not run";
    private string asrStageStatus = "Not run";
    private string translationStageStatus = "Not run";
    private string transcriptRevisionLabel = "No transcript loaded.";
    private string translationRevisionLabel = "No Spanish translation loaded.";
    private string translationRefreshStatus = "Not translated";
    private string statusMessage = "Open media to create a project, or open an existing .babelstudio folder.";
    private bool isBusy;

    public MainWindowViewModel()
    {
        Segments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(CanSaveTranscript));
            OnPropertyChanged(nameof(CanSaveTranslation));
        };
    }

    public ObservableCollection<TranscriptSegmentItem> Segments { get; } = [];

    public IReadOnlyList<TranscriptLanguageChoice> TranscriptLanguageOptions { get; } =
    [
        new("en", "English"),
        new("es", "Spanish")
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
            }
        }
    }

    public bool CanSaveTranscript => !IsBusy && currentTranscriptRevisionId is not null && Segments.Count > 0;

    public bool CanTranslate =>
        !IsBusy &&
        currentTranscriptRevisionId is not null &&
        string.Equals(SelectedTranscriptLanguageCode, "en", StringComparison.Ordinal);

    public bool CanSaveTranslation =>
        !IsBusy &&
        currentTranslationRevisionId is not null &&
        Segments.Count > 0;

    public bool CanCopyError => !string.IsNullOrWhiteSpace(lastErrorReport);

    public string? LastErrorReport => lastErrorReport;

    public string TranslateButtonText => currentTranslationRevisionId is null
        ? "Translate to Spanish"
        : "Re-translate to Spanish";

    public bool HasTranscriptLanguageChangePending =>
        !string.Equals(persistedTranscriptLanguageCode, SelectedTranscriptLanguageCode, StringComparison.Ordinal);

    public string TranscriptLanguageSummary =>
        SelectedTranscriptLanguageCode switch
        {
            "en" => "English transcript. Spanish translation is enabled.",
            "es" => "Spanish transcript. Milestone 7 only supports English-to-Spanish translation, so translation is disabled.",
            _ => "Choose whether the transcript is English or Spanish."
        };

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
        persistedTranscriptLanguageCode = NormalizeLanguageCode(state.TranscriptLanguage);
        SelectedTranscriptLanguageCode = persistedTranscriptLanguageCode;

        ProjectRootPath = loadedProjectRootPath;
        MediaPath = state.ProjectState.SourceReference?.OriginalPath ?? "Source media reference missing.";

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
            ? "No Spanish translation loaded."
            : $"Spanish revision {state.CurrentTranslationRevision.RevisionNumber} with {state.TranslatedSegments.Count} segment(s).";
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

        OnPropertyChanged(nameof(CanSaveTranscript));
        OnPropertyChanged(nameof(CanTranslate));
        OnPropertyChanged(nameof(CanSaveTranslation));
        OnPropertyChanged(nameof(TranslateButtonText));
        OnPropertyChanged(nameof(HasTranscriptLanguageChangePending));
        OnPropertyChanged(nameof(TranscriptLanguageSummary));
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
            "es",
            Segments.Select(segment => new EditedTranslatedSegment(segment.SegmentIndex, segment.TranslationText)).ToArray());
    }

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

    private static string BuildLoadedStatusMessage(TranscriptProjectState state)
    {
        if (state.CurrentTranscriptRevision is null)
        {
            return "Project loaded. No transcript revision is stored yet.";
        }

        if (state.CurrentTranslationRevision is not null)
        {
            return state.IsTranslationStale
                ? "Project loaded. Existing Spanish translation needs refresh after transcript changes."
                : "Project loaded. Edit transcript or translation text and save to create a new revision.";
        }

        return NormalizeLanguageCode(state.TranscriptLanguage) switch
        {
            "en" => "Project loaded. Translate to Spanish to create the first draft translation.",
            "es" => "Project loaded. Translation is disabled because Milestone 7 only supports English-to-Spanish translation.",
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
}
