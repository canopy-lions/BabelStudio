using System.Text.Json;
using BabelStudio.Application.Contracts;
using BabelStudio.Application.Projects;

namespace BabelStudio.Infrastructure.FileSystem;

public sealed class FileSystemArtifactStore : IArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string projectRootPath;

    public FileSystemArtifactStore(string projectRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRootPath);
        this.projectRootPath = Path.GetFullPath(projectRootPath);
    }

    public Task EnsureLayoutAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Directory.CreateDirectory(projectRootPath);
        foreach (string relativeDirectory in ProjectArtifactPaths.RequiredDirectories)
        {
            Directory.CreateDirectory(GetPath(relativeDirectory));
        }

        return Task.CompletedTask;
    }

    public ArtifactWriteHandle CreateWriteHandle(string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        string finalPath = GetPath(normalizedRelativePath);
        string extension = Path.GetExtension(finalPath);
        string tempFileName = $"{Path.GetFileNameWithoutExtension(finalPath)}.{Guid.NewGuid():N}.tmp{extension}";
        string tempPath = Path.Combine(GetPath("temp"), tempFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);
        return new ArtifactWriteHandle(normalizedRelativePath, finalPath, tempPath);
    }

    public Task CommitAsync(ArtifactWriteHandle handle, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(handle.TemporaryPath))
        {
            throw new FileNotFoundException("Temporary artifact file was not created.", handle.TemporaryPath);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(handle.FinalPath)!);
        File.Move(handle.TemporaryPath, handle.FinalPath, overwrite: true);
        return Task.CompletedTask;
    }

    public async Task WriteJsonAsync<T>(string relativePath, T value, CancellationToken cancellationToken)
    {
        ArtifactWriteHandle handle = CreateWriteHandle(relativePath);
        await using (var stream = new FileStream(
                         handle.TemporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 4096,
                         options: FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        await CommitAsync(handle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<T?> ReadJsonAsync<T>(string relativePath, CancellationToken cancellationToken)
    {
        string path = GetPath(relativePath);
        if (!File.Exists(path))
        {
            return default;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public string GetPath(string relativePath)
    {
        string normalizedRelativePath = NormalizeRelativePath(relativePath);
        return Path.GetFullPath(Path.Combine(projectRootPath, normalizedRelativePath));
    }

    public bool Exists(string relativePath) => File.Exists(GetPath(relativePath));

    private static string NormalizeRelativePath(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidOperationException($"Artifact path '{relativePath}' must be project-relative.");
        }

        string[] segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(static segment => segment == ".."))
        {
            throw new InvalidOperationException($"Artifact path '{relativePath}' cannot traverse parent directories.");
        }

        return Path.Combine(segments);
    }
}
