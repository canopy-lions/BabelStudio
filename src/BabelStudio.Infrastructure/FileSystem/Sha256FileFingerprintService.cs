using System.Security.Cryptography;
using BabelStudio.Application.Contracts;

namespace BabelStudio.Infrastructure.FileSystem;

public sealed class Sha256FileFingerprintService : IFileFingerprintService
{
    public async Task<FileFingerprint> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File was not found for fingerprinting.", fullPath);
        }

        await using FileStream stream = File.OpenRead(fullPath);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var fileInfo = new FileInfo(fullPath);

        return new FileFingerprint(
            Convert.ToHexString(hash).ToLowerInvariant(),
            fileInfo.Length,
            fileInfo.LastWriteTimeUtc);
    }
}
