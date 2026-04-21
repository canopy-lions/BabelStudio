using System.Security.Cryptography;
using System.Text.Json;
using BabelStudio.Application.Projects;
using BabelStudio.Infrastructure.FileSystem;

namespace BabelStudio.Infrastructure.Tests;

public sealed class FileSystemArtifactStoreTests
{
    [Fact]
    public async Task CommitAsync_moves_temp_file_atomically_into_place()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "AtomicWrite.babelstudio");
        var store = new FileSystemArtifactStore(projectRoot);
        await store.EnsureLayoutAsync(CancellationToken.None);
        var handle = store.CreateWriteHandle(ProjectArtifactPaths.WaveformSummaryRelativePath);

        await File.WriteAllTextAsync(handle.TemporaryPath, "{\"ok\":true}");
        Assert.False(File.Exists(handle.FinalPath));

        await store.CommitAsync(handle, CancellationToken.None);

        Assert.True(File.Exists(handle.FinalPath));
        Assert.False(File.Exists(handle.TemporaryPath));
    }

    [Fact]
    public async Task WriteJsonAsync_and_fingerprint_service_produce_expected_hash()
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "HashCheck.babelstudio");
        var store = new FileSystemArtifactStore(projectRoot);
        var fingerprintService = new Sha256FileFingerprintService();
        await store.EnsureLayoutAsync(CancellationToken.None);

        await store.WriteJsonAsync(
            ProjectArtifactPaths.WaveformSummaryRelativePath,
            new { ok = true, buckets = 4 },
            CancellationToken.None);

        string finalPath = store.GetPath(ProjectArtifactPaths.WaveformSummaryRelativePath);
        string expectedHash = Convert.ToHexString(
                SHA256.HashData(await File.ReadAllBytesAsync(finalPath)))
            .ToLowerInvariant();

        var fingerprint = await fingerprintService.ComputeAsync(finalPath, CancellationToken.None);

        Assert.Equal(expectedHash, fingerprint.Sha256);

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(finalPath));
        Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
    }

    [Theory]
    [InlineData(@"D:\absolute\artifact.json")]
    [InlineData(@"\\server\share\artifact.json")]
    public void CreateWriteHandle_RejectsAbsolutePath(string path)
    {
        string projectRoot = Path.Combine(Path.GetTempPath(), "BabelStudio.Infrastructure.Tests", Guid.NewGuid().ToString("N"), "AbsolutePath.babelstudio");
        var store = new FileSystemArtifactStore(projectRoot);

        Assert.Throws<InvalidOperationException>(() => store.CreateWriteHandle(path));
    }
}
