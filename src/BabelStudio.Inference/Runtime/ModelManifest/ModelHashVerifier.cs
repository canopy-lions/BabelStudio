using System.Security.Cryptography;

namespace BabelStudio.Inference.Runtime.ModelManifest;

public sealed class ModelHashVerifier
{
    public HashVerificationResult Verify(ModelManifest manifest, string filePath)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string expectedHash = manifest.Sha256.Trim();
        if (manifest.HashVerificationPolicy.Mode is HashVerificationMode.None)
        {
            return new HashVerificationResult(true, false, null, null, "Hash verification disabled by policy.");
        }

        if (string.IsNullOrWhiteSpace(expectedHash))
        {
            if (manifest.HashVerificationPolicy.Mode is HashVerificationMode.Required)
            {
                return new HashVerificationResult(false, false, null, null, "Hash verification is required but the manifest does not define sha256.");
            }

            return new HashVerificationResult(true, false, null, null, "Manifest does not define sha256; verification skipped.");
        }

        string actualHash = ComputeSha256(filePath);
        bool isMatch = actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        return new HashVerificationResult(
            IsValid: isMatch,
            WasVerified: true,
            ExpectedSha256: expectedHash,
            ActualSha256: actualHash,
            FailureReason: isMatch ? null : "Computed file hash did not match manifest sha256.");
    }

    private static string ComputeSha256(string filePath)
    {
        using FileStream stream = File.OpenRead(Path.GetFullPath(filePath));
        byte[] hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

public sealed record HashVerificationResult(
    bool IsValid,
    bool WasVerified,
    string? ExpectedSha256,
    string? ActualSha256,
    string? FailureReason);
