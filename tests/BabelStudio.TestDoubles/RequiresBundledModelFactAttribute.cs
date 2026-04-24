using System.Runtime.InteropServices;

namespace BabelStudio.TestDoubles;

/// <summary>
/// Marks an xUnit test that needs one or more on-disk bundled models under
/// the repo's gitignored <c>models/</c> directory. If any required path is
/// missing the test is skipped, so <c>dotnet test</c> works cleanly on
/// machines (and CI agents) that haven't downloaded the bundle.
/// </summary>
/// <remarks>
/// The attribute evaluates at test-discovery time via <see cref="FactAttribute.Skip"/>,
/// so missing models never touch the engine code paths under test.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresBundledModelFactAttribute : FactAttribute
{
    public RequiresBundledModelFactAttribute(params string[] relativePaths)
    {
        Skip = BundledModelSkipResolver.Resolve(relativePaths);
    }
}

/// <summary>
/// Theory variant of <see cref="RequiresBundledModelFactAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class RequiresBundledModelTheoryAttribute : TheoryAttribute
{
    public RequiresBundledModelTheoryAttribute(params string[] relativePaths)
    {
        Skip = BundledModelSkipResolver.Resolve(relativePaths);
    }
}

/// <summary>
/// Marks an xUnit test that only makes sense on Windows (for example, tests
/// that parse Windows-style path separators or assert Windows-specific I/O
/// behaviour). On non-Windows runs the test is skipped instead of failing.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "Windows-only test (skipped on non-Windows test runs).";
        }
    }
}

internal static class BundledModelSkipResolver
{
    internal static string? Resolve(string[] relativePaths)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);

        var repoRoot = FindRepoRoot();
        if (repoRoot is null)
        {
            return "Unable to locate BabelStudio.sln from the test runner base directory; skipping bundled-model test.";
        }

        foreach (var relative in relativePaths)
        {
            var full = Path.GetFullPath(Path.Combine(repoRoot, "models", relative));
            if (Directory.Exists(full) || File.Exists(full))
            {
                continue;
            }

            return $"Required bundled model not present at {Path.Combine("models", relative)}. Download the model bundle (gitignored under models/) to run this test.";
        }

        return null;
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "BabelStudio.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
