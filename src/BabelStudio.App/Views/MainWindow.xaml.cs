using BabelStudio.Composition;
using BabelStudio.App.ViewModels;
using BabelStudio.Application.Transcripts;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BabelStudio.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly TranscriptWorkspaceFactory workspaceFactory = new();
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

    private CancellationTokenSource? activeOperationCancellationSource;
    private TranscriptProjectService? currentService;
    private string? currentProjectRootPath;
    private bool isApplyingProjectState;

    public MainWindow()
    {
        ViewModel = new MainWindowViewModel();
        InitializeComponent();
        Closed += MainWindow_Closed;
    }

    public MainWindowViewModel ViewModel { get; }

    public void ReportUnhandledException(Exception exception, string context)
    {
        ViewModel.ReportException(exception, context);
        ViewModel.IsBusy = false;
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
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

            ApplyProjectState(state, projectRootPath);
        }, "Creating project, extracting audio, and generating transcript...");
    }

    private async void OpenProjectButton_Click(object sender, RoutedEventArgs e)
    {
        string? projectRootPath = await PickProjectFolderAsync().ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            return;
        }

        await RunAsync(async cancellationToken =>
        {
            currentService = workspaceFactory.Create(projectRootPath);
            currentProjectRootPath = projectRootPath;
            TranscriptProjectState state = await currentService.OpenAsync(cancellationToken).ConfigureAwait(true);
            ApplyProjectState(state, projectRootPath);
        }, "Opening project...");
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
            ApplyProjectState(state, currentProjectRootPath);
        }, "Saving transcript edits...");
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
                ApplyProjectState(updatedLanguageState, currentProjectRootPath);
            }

            TranscriptProjectState state = await currentService.GenerateTranslationAsync(
                new GenerateTranslationRequest(ViewModel.SelectedTranscriptLanguageCode ?? string.Empty, "es"),
                cancellationToken).ConfigureAwait(true);
            ApplyProjectState(state, currentProjectRootPath);
        }, "Generating Spanish translation...");
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
            ApplyProjectState(state, currentProjectRootPath);
        }, "Saving translation edits...");
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
            ApplyProjectState(state, currentProjectRootPath);
        }, "Saving transcript language...");
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
}
