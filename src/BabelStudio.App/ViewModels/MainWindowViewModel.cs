using System.Collections.ObjectModel;
using BabelStudio.Application.Transcripts;
using BabelStudio.Domain;

namespace BabelStudio.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private Guid? currentTranscriptRevisionId;
    private string projectNameDraft = "New Project";
    private string projectRootPath = "No project loaded.";
    private string mediaPath = "No media selected.";
    private string sourceStatus = "No project loaded.";
    private string vadStageStatus = "Not run";
    private string asrStageStatus = "Not run";
    private string transcriptRevisionLabel = "No transcript loaded.";
    private string statusMessage = "Open media to create a project, or open an existing .babelstudio folder.";
    private bool isBusy;

    public ObservableCollection<TranscriptSegmentItem> Segments { get; } = [];

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

    public string TranscriptRevisionLabel
    {
        get => transcriptRevisionLabel;
        private set => SetProperty(ref transcriptRevisionLabel, value);
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
            }
        }
    }

    public bool CanSaveTranscript => !IsBusy && currentTranscriptRevisionId is not null && Segments.Count > 0;

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
        TranscriptRevisionLabel = state.CurrentTranscriptRevision is null
            ? "No transcript loaded."
            : $"Revision {state.CurrentTranscriptRevision.RevisionNumber} with {state.TranscriptSegments.Count} segment(s).";

        Segments.Clear();
        foreach (TranscriptSegmentItem item in state.TranscriptSegments
                     .OrderBy(segment => segment.SegmentIndex)
                     .Select(segment => new TranscriptSegmentItem(segment)))
        {
            Segments.Add(item);
        }

        ProjectNameDraft = state.ProjectState.Project.Name;
        StatusMessage = state.CurrentTranscriptRevision is null
            ? "Project loaded. No transcript revision is stored yet."
            : "Project loaded. Edit transcript text and save to create a new revision.";
        OnPropertyChanged(nameof(CanSaveTranscript));
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

        return latest.Status switch
        {
            StageRunStatus.Completed => "Completed",
            StageRunStatus.Failed => $"Failed: {latest.FailureReason}",
            _ => "Running"
        };
    }
}
