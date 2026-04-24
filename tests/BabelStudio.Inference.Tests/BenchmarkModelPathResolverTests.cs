using BabelStudio.Inference.Onnx;
using BabelStudio.Inference.Runtime.ModelManifest;

namespace BabelStudio.Inference.Tests;

public sealed class BenchmarkModelPathResolverTests
{
    [Fact]
    public void Discover_when_manifest_alias_has_no_onnx_file_returns_missing_entry_error()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "BabelStudio.Inference.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            string manifestDirectory = Path.Combine(tempDirectory, "manifest");
            Directory.CreateDirectory(manifestDirectory);
            string manifestPath = Path.Combine(manifestDirectory, "bundled-models.manifest.json");
            File.WriteAllText(
                manifestPath,
                """
                {
                  "models": [
                    {
                      "model_id": "example/temp-diarizer",
                      "task": "diarization",
                      "license": "non-commercial",
                      "commercial_allowed": false,
                      "redistribution_allowed": true,
                      "requires_attribution": true,
                      "requires_user_consent": false,
                      "voice_cloning": false,
                      "commercial_safe_mode": false,
                      "source_url": "https://example.invalid/temp-diarizer",
                      "revision": "pending-export",
                      "sha256": "",
                      "aliases": [ "temp-diarizer" ],
                      "root_path": "../models/temp-diarizer",
                      "benchmark_entry": "onnx/model.onnx",
                      "variants": []
                    }
                  ]
                }
                """);

            BundledModelManifestRegistry registry = BundledModelManifestRegistry.Load(manifestPath);
            var resolver = new BenchmarkModelPathResolver(registry);

            BenchmarkModelResolutionResult result = resolver.Discover("temp-diarizer");

            Assert.Empty(result.Candidates);
            Assert.Equal("manifest:temp-diarizer", result.ScopeKey);
            Assert.Contains("no ONNX entry point exists on disk", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Throws<FileNotFoundException>(() => resolver.ResolveSingle("temp-diarizer"));
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }
}
