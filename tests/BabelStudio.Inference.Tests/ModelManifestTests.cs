using BabelStudio.Inference.Runtime.ModelManifest;
using BabelStudio.Infrastructure.FileSystem;
using BabelStudio.Infrastructure.Persistence.Repositories;
using BabelStudio.Infrastructure.Settings;
using BabelStudio.Domain;

namespace BabelStudio.Inference.Tests;

public sealed class ModelManifestLoaderTests
{
    [Fact]
    public void LoadCatalog_LoadsValidManifest()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "models": [
                {
                  "model_id": "example/model",
                  "task": "asr",
                  "license": "MIT",
                  "commercial_allowed": true,
                  "redistribution_allowed": true,
                  "requires_attribution": false,
                  "requires_user_consent": false,
                  "voice_cloning": false,
                  "commercial_safe_mode": true,
                  "source_url": "https://example.invalid/model",
                  "revision": "main",
                  "sha256": "abc123",
                  "aliases": [ "example-model" ],
                  "root_path": "../../../../models/example-model",
                  "benchmark_entry": "onnx/model.onnx",
                  "variants": [
                    {
                      "alias": "fp16",
                      "entry_path": "onnx/model_fp16.onnx",
                      "sha256": "def456"
                    }
                  ],
                  "hash_verification": {
                    "mode": "required",
                    "algorithm": "SHA-256"
                  }
                }
              ]
            }
            """);

        try
        {
            ModelManifestCatalog catalog = ModelManifestLoader.LoadCatalog(manifestPath);

            ModelManifest manifest = Assert.Single(catalog.Models);
            Assert.Equal("example/model", manifest.ModelId);
            Assert.Equal(ModelTask.Asr, manifest.Task);
            Assert.Equal(ModelLicenseKind.Mit, manifest.License);
            Assert.True(manifest.CommercialAllowed);
            Assert.Single(manifest.Aliases);
            Assert.Single(manifest.Variants);
            Assert.Equal(HashVerificationMode.Required, manifest.HashVerificationPolicy.Mode);
            Assert.Equal("SHA-256", manifest.HashVerificationPolicy.Algorithm);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsInvalidLicense()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "asr",
              "license": "Proprietary",
              "commercial_allowed": true,
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": false,
              "commercial_safe_mode": false,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "variants": []
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains(".license", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Proprietary", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsVoiceCloningWithoutConsent()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "tts",
              "license": "MIT",
              "commercial_allowed": true,
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": true,
              "commercial_safe_mode": true,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "variants": []
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains("requires_user_consent", exception.Message, StringComparison.Ordinal);
            Assert.Contains("voice_cloning", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsDuplicateAliases()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "asr",
              "license": "MIT",
              "commercial_allowed": true,
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": false,
              "commercial_safe_mode": true,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "aliases": [ "example-model", "example-model" ],
              "variants": []
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains("duplicate alias", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsInvalidTask()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "embedding",
              "license": "MIT",
              "commercial_allowed": true,
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": false,
              "commercial_safe_mode": true,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "variants": []
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains(".task", exception.Message, StringComparison.Ordinal);
            Assert.Contains("embedding", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsInvalidHashVerificationMode()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "asr",
              "license": "MIT",
              "commercial_allowed": true,
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": false,
              "commercial_safe_mode": true,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "variants": [],
              "hash_verification": {
                "mode": "always",
                "algorithm": "SHA-256"
              }
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains("hash_verification.mode", exception.Message, StringComparison.Ordinal);
            Assert.Contains("always", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_RejectsMissingRequiredField()
    {
        string manifestPath = WriteTempManifest(
            """
            {
              "model_id": "example/model",
              "task": "asr",
              "license": "MIT",
              "redistribution_allowed": true,
              "requires_attribution": false,
              "requires_user_consent": false,
              "voice_cloning": false,
              "commercial_safe_mode": true,
              "source_url": "",
              "revision": "",
              "sha256": "",
              "variants": []
            }
            """);

        try
        {
            ModelManifestValidationException exception = Assert.Throws<ModelManifestValidationException>(
                () => ModelManifestLoader.LoadCatalog(manifestPath));

            Assert.Contains("commercial_allowed", exception.Message, StringComparison.Ordinal);
            Assert.Contains("missing required field", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(manifestPath);
        }
    }

    [Fact]
    public void LoadCatalog_LoadsBundledManifest()
    {
        string repoRoot = FindRepoRoot();
        string manifestPath = Path.Combine(
            repoRoot,
            "src", "BabelStudio.Inference", "Runtime", "ModelManifest", "bundled-models.manifest.json");

        ModelManifestCatalog catalog = ModelManifestLoader.LoadCatalog(manifestPath);

        Assert.NotEmpty(catalog.Models);
        Assert.Contains(catalog.Models, manifest => manifest.ModelId.Equals("onnx-community/silero-vad", StringComparison.Ordinal));
        Assert.Contains(catalog.Models, manifest => manifest.License is ModelLicenseKind.Unknown);
    }

    private static string WriteTempManifest(string json)
    {
        string manifestPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");
        File.WriteAllText(manifestPath, json);
        return manifestPath;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BabelStudio.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

public sealed class CommercialSafeEvaluatorTests
{
    private readonly CommercialSafeEvaluator evaluator = new();

    [Fact]
    public void Evaluate_RejectsUnknownLicense()
    {
        CommercialSafeEvaluation result = evaluator.Evaluate(CreateManifest(license: ModelLicenseKind.Unknown));

        Assert.False(result.IsCommercialSafe);
        Assert.Contains(result.Reasons, reason => reason.Contains("Unknown-license", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RejectsNonCommercialLicense()
    {
        CommercialSafeEvaluation result = evaluator.Evaluate(CreateManifest(license: ModelLicenseKind.NonCommercial, commercialAllowed: false));

        Assert.False(result.IsCommercialSafe);
        Assert.Contains(result.Reasons, reason => reason.Contains("Non-commercial", StringComparison.Ordinal));
    }

    [Fact]
    public void Evaluate_RequiresConsentForVoiceCloning()
    {
        CommercialSafeEvaluation result = evaluator.Evaluate(CreateManifest(
            task: ModelTask.Tts,
            voiceCloning: true,
            requiresUserConsent: true));

        Assert.True(result.IsCommercialSafe);
        Assert.True(result.RequiresUserConsent);
    }

    [Fact]
    public void Evaluate_PreservesAttributionFlag()
    {
        CommercialSafeEvaluation result = evaluator.Evaluate(CreateManifest(requiresAttribution: true));

        Assert.True(result.IsCommercialSafe);
        Assert.True(result.RequiresAttribution);
    }

    private static ModelManifest CreateManifest(
        ModelTask task = ModelTask.Asr,
        ModelLicenseKind license = ModelLicenseKind.Mit,
        bool commercialAllowed = true,
        bool requiresAttribution = false,
        bool requiresUserConsent = false,
        bool voiceCloning = false) =>
        new(
            ModelId: "example/model",
            Task: task,
            License: license,
            CommercialAllowed: commercialAllowed,
            RedistributionAllowed: true,
            RequiresAttribution: requiresAttribution,
            RequiresUserConsent: requiresUserConsent,
            VoiceCloning: voiceCloning,
            CommercialSafeMode: commercialAllowed && license is not ModelLicenseKind.Unknown and not ModelLicenseKind.NonCommercial,
            SourceUrl: "",
            Revision: "",
            Sha256: "",
            Variants: [],
            Aliases: [],
            RootPath: null,
            BenchmarkEntry: null,
            HashVerificationPolicy: new HashVerificationPolicy(HashVerificationMode.VerifyIfShaPresent, "SHA-256"));
}

public sealed class ModelHashVerifierTests
{
    [Fact]
    public void Verify_RequiredPolicyFailsWhenShaMissing()
    {
        var manifest = CreateManifest(HashVerificationMode.Required, sha256: "");
        var verifier = new ModelHashVerifier();
        string filePath = WriteTempFile([1, 2, 3]);

        try
        {
            HashVerificationResult result = verifier.Verify(manifest, filePath);

            Assert.False(result.IsValid);
            Assert.False(result.WasVerified);
            Assert.Contains("required", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Verify_VerifiesMatchingSha()
    {
        string filePath = WriteTempFile([1, 2, 3, 4]);
        string sha = new Sha256FileHasher().Compute(filePath);
        var manifest = CreateManifest(HashVerificationMode.VerifyIfShaPresent, sha);
        var verifier = new ModelHashVerifier();

        try
        {
            HashVerificationResult result = verifier.Verify(manifest, filePath);

            Assert.True(result.IsValid);
            Assert.True(result.WasVerified);
            Assert.Equal(sha, result.ActualSha256);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public void Verify_ReportsHashMismatch()
    {
        string filePath = WriteTempFile([5, 6, 7, 8]);
        var manifest = CreateManifest(HashVerificationMode.VerifyIfShaPresent, sha256: "deadbeef");
        var verifier = new ModelHashVerifier();

        try
        {
            HashVerificationResult result = verifier.Verify(manifest, filePath);

            Assert.False(result.IsValid);
            Assert.True(result.WasVerified);
            Assert.Contains("did not match", result.FailureReason!, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    private static ModelManifest CreateManifest(HashVerificationMode mode, string sha256) =>
        new(
            ModelId: "example/model",
            Task: ModelTask.Asr,
            License: ModelLicenseKind.Mit,
            CommercialAllowed: true,
            RedistributionAllowed: true,
            RequiresAttribution: false,
            RequiresUserConsent: false,
            VoiceCloning: false,
            CommercialSafeMode: true,
            SourceUrl: "",
            Revision: "",
            Sha256: sha256,
            Variants: [],
            Aliases: [],
            RootPath: null,
            BenchmarkEntry: null,
            HashVerificationPolicy: new HashVerificationPolicy(mode, "SHA-256"));

    private static string WriteTempFile(byte[] bytes)
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }
}

public sealed class LocalModelCacheRecordStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsRecords()
    {
        string rootPath = Path.Combine(Path.GetTempPath(), $"babelstudio-cache-{Guid.NewGuid():N}");
        var storagePaths = new BabelStudioStoragePaths(rootPath);
        var store = new LocalModelCacheRecordStore(storagePaths);
        LocalModelCacheRecord[] records =
        [
            new("example/model", @"D:\models\example", "main", "abc123", DateTimeOffset.UtcNow)
        ];

        try
        {
            await store.SaveAsync(records);
            IReadOnlyList<LocalModelCacheRecord> loaded = await store.LoadAsync();

            LocalModelCacheRecord record = Assert.Single(loaded);
            Assert.Equal(records[0].ModelId, record.ModelId);
            Assert.Equal(records[0].RootPath, record.RootPath);
            Assert.Equal(records[0].Sha256, record.Sha256);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }
}
