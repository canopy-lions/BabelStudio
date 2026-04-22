using BabelStudio.App.ViewModels;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;
using BabelStudio.Application.Transcripts;
using BabelStudio.Composition;
using BabelStudio.Media.Playback;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BabelStudio.App.Views;

public sealed partial class MainWindow : Window
{
    private static readonly HashSet<string> ReservedProjectFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9"
    };

    private readonly StudioShellServices shellServices;
    private readonly DispatcherQueueTimer playbackTimer;
    private readonly TranscriptWorkspaceFactory workspaceFactory = new();
    private CancellationTokenSource? activeOperationCancellationSource;
    private TranscriptProjectService? currentService;
    private string? currentProjectRootPath;
    private bool isApplyingProjectState;

    public MainWindow()
    {
        shellServices = new StudioShellFactory().Create();
        ViewModel = new MainWindowViewModel();
        InitializeComponent();
        shellServices.PlaybackService.TryAttachHost(SourcePlayerElement);

        playbackTimer = DispatcherQueue.CreateTimer();
        playbackTimer.Interval = TimeSpan.FromMilliseconds(250);
        playbackTimer.Tick += PlaybackTimer_Tick;

        Closed += MainWindow_Closed;
        _ = InitializeShellAsync();
    }

    public MainWindowViewModel ViewModel { get; }

    public void ReportUnhandledException(Exception exception, string context)
    {
        ViewModel.ReportException(exception, context);
        ViewModel.IsBusy = false;
    }

    private async Task InitializeShellAsync()
    {
        try
        {
            StudioSettings settings = await shellServices.SettingsService.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            ViewModel.ApplySettings(settings);
            ApplyWindowLayout(settings.WindowLayout);
        }
        catch (Exception ex)
        {
            ViewModel.ReportException(ex, "Loading app settings");
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        playbackTimer.Stop();
        activeOperationCancellationSource?.Cancel();
        activeOperationCancellationSource?.Dispose();
        activeOperationCancellationSource = null;
        Closed -= MainWindow_Closed;
    }

    private async void OpenMediaButton_Click(object sender, RoutedEventArgs e)
    {
        string? mediaPath = await PickMediaFileAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(mediaPath))
        {
            return;
        }

        ViewModel.SetProjectNameFromMedia(mediaPath);
        string projectRootPath = BuildProjectRootPath(mediaPath, ViewModel.ProjectNameDraft);
        await RunAsync(async cancellationToken =>
        {
            currentService = workspaceFactory.Create(projectRootPath);
            currentProjectRootPath = projectRootPath;
            TranscriptProjectState state = await currentService.CreateAsync(
                new CreateTranscriptProjectRequest(ViewModel.ProjectNameDraft, mediaPath),
                cancellationToken).ConfigureAwait(true);

            await CompleteProjectLoadAsync(state, projectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Creating project, extracting audio, and generating transcript...");
    }

    private async void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        string? projectRootPath = await PickProjectFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            return;
        }

        await OpenProjectAsync(projectRootPath, "Opening project...").ConfigureAwait(true);
    }

    private async void RecentProjectsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not RecentProjectItem item)
        {
            return;
        }

        if (!Directory.Exists(item.ProjectPath))
        {
            ViewModel.StatusMessage = $"Recent project path does not exist: {item.ProjectPath}";
            return;
        }

        await OpenProjectAsync(item.ProjectPath, "Opening recent project...").ConfigureAwait(true);
    }

    private async Task OpenProjectAsync(string projectRootPath, string busyMessage)
    {
        await RunAsync(async cancellationToken =>
        {
            currentService = workspaceFactory.Create(projectRootPath);
            currentProjectRootPath = projectRootPath;
            TranscriptProjectState state = await currentService.OpenAsync(cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, projectRootPath, cancellationToken).ConfigureAwait(true);
        }, busyMessage).ConfigureAwait(true);
    }

    private async void SaveTranscriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            ViewModel.StatusMessage = "Load a project before saving transcript edits.";
            return;
        }

        SaveTranscriptEditsRequest? request = ViewModel.CreateSaveRequest();
        if (request is null)
        {
            ViewModel.StatusMessage = "There is no transcript revision to save.";
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.SaveEditsAsync(request, cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Saving transcript edits...").ConfigureAwait(true);
    }

    private async void TranslateButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            ViewModel.StatusMessage = "Load a project before generating a translation.";
            return;
        }

        if (!ViewModel.CanTranslate)
        {
            ViewModel.StatusMessage = ViewModel.TranscriptLanguageSummary;
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            if (ViewModel.HasTranscriptLanguageChangePending)
            {
                TranscriptProjectState updatedLanguageState = await currentService.SetTranscriptLanguageAsync(
                    new SetTranscriptLanguageRequest(ViewModel.SelectedTranscriptLanguageCode),
                    cancellationToken).ConfigureAwait(true);
                await CompleteProjectLoadAsync(updatedLanguageState, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
            }

            string targetLanguageCode = ViewModel.GetRequestedTranslationTargetLanguageCode()
                ?? throw new InvalidOperationException("Choose whether the transcript is English or Spanish before starting translation.");

            TranscriptProjectState state = await currentService.GenerateTranslationAsync(
                new GenerateTranslationRequest(ViewModel.SelectedTranscriptLanguageCode ?? string.Empty, targetLanguageCode),
                cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, $"Generating {GetLanguageDisplayName(ViewModel.GetRequestedTranslationTargetLanguageCode())} translation...").ConfigureAwait(true);
    }

    private async void SaveTranslationButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            ViewModel.StatusMessage = "Load a project before saving translation edits.";
            return;
        }

        SaveTranslationEditsRequest? request = ViewModel.CreateSaveTranslationRequest();
        if (request is null)
        {
            ViewModel.StatusMessage = "There is no translation revision to save.";
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.SaveTranslationEditsAsync(request, cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Saving translation edits...").ConfigureAwait(true);
    }

    private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        await RunAsync(async cancellationToken =>
        {
            StudioSettings settings = ViewModel.CreateSettings(CaptureWindowLayout());
            await shellServices.SettingsService.SaveAsync(settings, cancellationToken).ConfigureAwait(true);
            ViewModel.ApplySettings(settings);
            ViewModel.StatusMessage = "Settings saved.";
        }, "Saving settings...").ConfigureAwait(true);
    }

    private async void PlaybackSpeedComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaybackSpeedComboBox.SelectedValue is not double playbackRate)
        {
            return;
        }

        await shellServices.PlaybackService.SetPlaybackRateAsync(playbackRate, CancellationToken.None).ConfigureAwait(true);
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        await shellServices.PlaybackService.PlayAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        await shellServices.PlaybackService.PauseAsync(CancellationToken.None).ConfigureAwait(true);
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }

    private async void SegmentsListView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is TranscriptSegmentItem item)
        {
            await SeekToSegmentAsync(item).ConfigureAwait(true);
        }
    }

    private async void JumpToSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TranscriptSegmentItem item })
        {
            await SeekToSegmentAsync(item).ConfigureAwait(true);
        }
    }

    private async void JumpToActiveSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        TranscriptSegmentItem? active = ViewModel.Segments.FirstOrDefault(segment => segment.IsActive);
        if (active is not null)
        {
            await SeekToSegmentAsync(active).ConfigureAwait(true);
        }
    }

    private async void SplitSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            return;
        }

        if (sender is not FrameworkElement { Tag: TranscriptSegmentItem item })
        {
            return;
        }

        SplitTranscriptSegmentRequest? request = ViewModel.CreateSplitRequest(item, ViewModel.PlaybackPositionSeconds);
        if (request is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.SplitSegmentAsync(request, cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Splitting segment at the playback cursor...").ConfigureAwait(true);
    }

    private async void TrimSegmentButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            return;
        }

        if (sender is not FrameworkElement { Tag: TranscriptSegmentItem item })
        {
            return;
        }

        TrimTranscriptSegmentRequest? request = ViewModel.CreateTrimRequest(item);
        if (request is null)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.TrimSegmentAsync(request, cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Applying segment timing edits...").ConfigureAwait(true);
    }

    private async void MergeSegmentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (currentService is null || string.IsNullOrWhiteSpace(currentProjectRootPath))
        {
            return;
        }

        MergeTranscriptSegmentsRequest? request = ViewModel.CreateMergeRequest(
            SegmentsListView.SelectedItems.OfType<TranscriptSegmentItem>().ToArray());
        if (request is null)
        {
            ViewModel.StatusMessage = "Select exactly two adjacent segments to merge.";
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.MergeSegmentsAsync(request, cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Merging selected segments...").ConfigureAwait(true);
    }

    private async void TranscriptLanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isApplyingProjectState ||
            currentService is null ||
            string.IsNullOrWhiteSpace(currentProjectRootPath) ||
            !ViewModel.HasTranscriptLanguageChangePending)
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            TranscriptProjectState state = await currentService.SetTranscriptLanguageAsync(
                new SetTranscriptLanguageRequest(ViewModel.SelectedTranscriptLanguageCode),
                cancellationToken).ConfigureAwait(true);
            await CompleteProjectLoadAsync(state, currentProjectRootPath, cancellationToken).ConfigureAwait(true);
        }, "Saving transcript language...").ConfigureAwait(true);
    }

    private void CopyErrorButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.LastErrorReport))
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(ViewModel.LastErrorReport);
        Clipboard.SetContent(dataPackage);
        Clipboard.Flush();
        ViewModel.StatusMessage = "Error details copied to clipboard.";
    }

    private async Task CompleteProjectLoadAsync(
        TranscriptProjectState state,
        string projectRootPath,
        CancellationToken cancellationToken)
    {
        TranscriptProjectState resolvedState = await ResolveMissingSourceAsync(state, cancellationToken).ConfigureAwait(true);
        ApplyProjectState(resolvedState, projectRootPath);
        await UpdateRecentProjectsAsync(projectRootPath, resolvedState.ProjectState.Project.Name, cancellationToken).ConfigureAwait(true);
        await OpenPlaybackAsync(resolvedState, cancellationToken).ConfigureAwait(true);
    }

    private async Task<TranscriptProjectState> ResolveMissingSourceAsync(
        TranscriptProjectState state,
        CancellationToken cancellationToken)
    {
        if (currentService is null ||
            state.ProjectState.SourceStatus is not SourceMediaStatus.Missing)
        {
            return state;
        }

        string? relocatedPath = await PickMediaFileAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(relocatedPath))
        {
            return state;
        }

        return await currentService.RelocateSourceAsync(
            new RelocateTranscriptSourceRequest(relocatedPath),
            cancellationToken).ConfigureAwait(true);
    }

    private async Task UpdateRecentProjectsAsync(
        string projectRootPath,
        string projectName,
        CancellationToken cancellationToken)
    {
        StudioSettings updatedSettings = await shellServices.SettingsService.TouchRecentProjectAsync(
            projectRootPath,
            projectName,
            cancellationToken).ConfigureAwait(true);
        ViewModel.ApplySettings(updatedSettings);
    }

    private async Task OpenPlaybackAsync(TranscriptProjectState state, CancellationToken cancellationToken)
    {
        playbackTimer.Stop();
        shellServices.PlaybackService.TryAttachHost(SourcePlayerElement);

        if (state.ProjectState.SourceReference is null ||
            state.ProjectState.SourceStatus is not SourceMediaStatus.Available)
        {
            ViewModel.ApplyPlaybackAssessment(null, hasBackend: false);
            return;
        }

        PlaybackOpenResult result = await shellServices.PlaybackService.OpenAsync(
            new MediaSourceDescriptor(
                state.ProjectState.SourceReference.OriginalPath,
                state.ProjectState.SourceReference.Probe),
            cancellationToken).ConfigureAwait(true);

        shellServices.PlaybackService.TryAttachHost(SourcePlayerElement);
        ViewModel.ApplyPlaybackAssessment(result.Assessment, result.IsBackendAvailable);
        ViewModel.ApplyPlaybackSnapshot(result.Snapshot);
        WaveformCanvas.Invalidate();

        if (result.IsBackendAvailable)
        {
            playbackTimer.Start();
        }
    }

    private async void PlaybackTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }

    private async Task RefreshPlaybackSnapshotAsync()
    {
        PlaybackSnapshot snapshot = await shellServices.PlaybackService.GetSnapshotAsync(CancellationToken.None).ConfigureAwait(true);
        ViewModel.ApplyPlaybackSnapshot(snapshot);
        WaveformCanvas.Invalidate();
    }

    private async Task SeekToSegmentAsync(TranscriptSegmentItem item)
    {
        await shellServices.PlaybackService.SeekAsync(TimeSpan.FromSeconds(item.StartSeconds), CancellationToken.None).ConfigureAwait(true);
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string busyMessage)
    {
        using var operationCancellationSource = new CancellationTokenSource();
        activeOperationCancellationSource = operationCancellationSource;
        ViewModel.BeginOperation(busyMessage);

        try
        {
            await action(operationCancellationSource.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (operationCancellationSource.IsCancellationRequested)
        {
            ViewModel.StatusMessage = "Operation canceled.";
        }
        catch (Exception ex)
        {
            ViewModel.ReportException(ex, busyMessage);
        }
        finally
        {
            if (ReferenceEquals(activeOperationCancellationSource, operationCancellationSource))
            {
                activeOperationCancellationSource = null;
            }

            ViewModel.IsBusy = false;
        }
    }

    private async Task<string?> PickMediaFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.VideosLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add(".mp4");
        picker.FileTypeFilter.Add(".mov");
        picker.FileTypeFilter.Add(".mkv");
        picker.FileTypeFilter.Add(".wav");
        picker.FileTypeFilter.Add(".mp3");
        picker.FileTypeFilter.Add(".m4a");
        picker.FileTypeFilter.Add(".aac");
        picker.FileTypeFilter.Add(".flac");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFile? file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private static string GetLanguageDisplayName(string? languageCode) =>
        (languageCode ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "en" => "English",
            "es" => "Spanish",
            _ => "translation"
        };

    private async Task<string?> PickProjectFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            ViewMode = PickerViewMode.List
        };

        picker.FileTypeFilter.Add("*");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        StorageFolder? folder = await picker.PickSingleFolderAsync();
        return folder?.Path;
    }

    private static string BuildProjectRootPath(string mediaPath, string projectName)
    {
        string directory = Path.GetDirectoryName(mediaPath)
            ?? throw new InvalidOperationException("Source media path does not have a parent directory.");

        string sanitizedName = SanitizeProjectFolderName(projectName, Path.GetFileNameWithoutExtension(mediaPath));

        return Path.Combine(directory, $"{sanitizedName}.babelstudio");
    }

    private static string SanitizeProjectFolderName(string projectName, string fallbackName)
    {
        string sanitized = NormalizeProjectFolderName(projectName);
        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = NormalizeProjectFolderName(fallbackName);
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "Project";
        }

        if (ReservedProjectFolderNames.Contains(sanitized) || sanitized is "." or "..")
        {
            sanitized = $"{sanitized}_";
        }

        return sanitized;
    }

    private static string NormalizeProjectFolderName(string value)
    {
        string replaced = new(value.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        return replaced.Trim().TrimEnd('.', ' ');
    }

    private void ApplyProjectState(TranscriptProjectState state, string projectRootPath)
    {
        isApplyingProjectState = true;
        try
        {
            ViewModel.ApplyProjectState(state, projectRootPath);
        }
        finally
        {
            isApplyingProjectState = false;
        }
    }

    private WindowLayoutSettings CaptureWindowLayout()
    {
        try
        {
            AppWindow appWindow = GetAppWindow();
            return new WindowLayoutSettings(
                appWindow.Size.Width,
                appWindow.Size.Height,
                appWindow.Presenter is OverlappedPresenter presenter &&
                presenter.State == OverlappedPresenterState.Maximized);
        }
        catch
        {
            return new WindowLayoutSettings(null, null, IsMaximized: false);
        }
    }

    private void ApplyWindowLayout(WindowLayoutSettings windowLayout)
    {
        try
        {
            AppWindow appWindow = GetAppWindow();
            if (windowLayout.Width is double width && windowLayout.Height is double height)
            {
                appWindow.Resize(new SizeInt32((int)Math.Round(width), (int)Math.Round(height)));
            }

            if (windowLayout.IsMaximized && appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.Maximize();
            }
        }
        catch
        {
        }
    }

    private AppWindow GetAppWindow()
    {
        IntPtr hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    private void WaveformCanvas_Draw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        args.DrawingSession.Clear(ColorHelper.FromArgb(255, 18, 24, 32));

        WaveformSummary? waveform = ViewModel.CurrentWaveformSummary;
        if (waveform is null || waveform.Peaks.Count == 0 || sender.ActualWidth <= 0d || sender.ActualHeight <= 0d)
        {
            return;
        }

        float width = (float)sender.ActualWidth;
        float height = (float)sender.ActualHeight;
        float centerY = height / 2f;
        float step = width / Math.Max(waveform.Peaks.Count, 1);

        for (int index = 0; index < waveform.Peaks.Count; index++)
        {
            float amplitude = Math.Clamp(waveform.Peaks[index], 0f, 1f);
            float barHeight = Math.Max(1f, amplitude * height);
            float x = index * step;
            args.DrawingSession.DrawLine(x, centerY - (barHeight / 2f), x, centerY + (barHeight / 2f), Colors.DeepSkyBlue, Math.Max(1f, step * 0.6f));
        }

        foreach (TranscriptSegmentItem segment in ViewModel.Segments)
        {
            float startX = WaveformMapping.TimeToPixel(segment.StartSeconds, waveform.DurationSeconds, width);
            float endX = WaveformMapping.TimeToPixel(segment.EndSeconds, waveform.DurationSeconds, width);
            args.DrawingSession.DrawLine(startX, 0f, startX, height, Colors.OrangeRed, 1f);
            args.DrawingSession.DrawLine(endX, 0f, endX, height, Colors.OrangeRed, 1f);
        }

        float cursorX = WaveformMapping.TimeToPixel(ViewModel.PlaybackPositionSeconds, waveform.DurationSeconds, width);
        args.DrawingSession.DrawLine(cursorX, 0f, cursorX, height, Colors.Gold, 2f);
    }

    private async void WaveformCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        WaveformSummary? waveform = ViewModel.CurrentWaveformSummary;
        if (waveform is null || sender is not CanvasControl canvas)
        {
            return;
        }

        float width = (float)canvas.ActualWidth;
        if (width <= 0f)
        {
            return;
        }

        float x = (float)e.GetCurrentPoint(canvas).Position.X;
        double timeSeconds = WaveformMapping.PixelToTime(x, waveform.DurationSeconds, width);
        await shellServices.PlaybackService.SeekAsync(TimeSpan.FromSeconds(timeSeconds), CancellationToken.None).ConfigureAwait(true);
        await RefreshPlaybackSnapshotAsync().ConfigureAwait(true);
    }
}
