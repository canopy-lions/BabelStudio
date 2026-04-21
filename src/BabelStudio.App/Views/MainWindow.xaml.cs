using BabelStudio.App.Composition;
using BabelStudio.App.ViewModels;
using BabelStudio.Application.Transcripts;
using Microsoft.UI.Xaml;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace BabelStudio.App.Views;

public sealed partial class MainWindow : Window
{
    private readonly TranscriptWorkspaceFactory workspaceFactory = new();
    private TranscriptProjectService? currentService;
    private string? currentProjectRootPath;

    public MainWindow()
    {
        ViewModel = new MainWindowViewModel();
        InitializeComponent();
    }

    public MainWindowViewModel ViewModel { get; }

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

            ViewModel.ApplyProjectState(state, projectRootPath);
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
            ViewModel.ApplyProjectState(state, projectRootPath);
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
            ViewModel.ApplyProjectState(state, currentProjectRootPath);
        }, "Saving transcript edits...");
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, string busyMessage)
    {
        ViewModel.IsBusy = true;
        ViewModel.StatusMessage = busyMessage;

        try
        {
            await action(CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ViewModel.StatusMessage = ex.Message;
        }
        finally
        {
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

        string sanitizedName = new(projectName.Trim().Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch).ToArray());
        if (string.IsNullOrWhiteSpace(sanitizedName))
        {
            sanitizedName = Path.GetFileNameWithoutExtension(mediaPath);
        }

        return Path.Combine(directory, $"{sanitizedName}.babelstudio");
    }
}
