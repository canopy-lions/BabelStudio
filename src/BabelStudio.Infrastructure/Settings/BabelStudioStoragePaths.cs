namespace BabelStudio.Infrastructure.Settings;

public sealed class BabelStudioStoragePaths
{
    public BabelStudioStoragePaths(string? localAppDataRoot = null)
    {
        string root = string.IsNullOrWhiteSpace(localAppDataRoot)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : localAppDataRoot;

        RootDirectory = Path.Combine(root, "BabelStudio");
        ModelCacheDirectory = Path.Combine(RootDirectory, "model-cache");
        ModelCacheIndexPath = Path.Combine(ModelCacheDirectory, "model-cache-records.json");
    }

    public string RootDirectory { get; }

    public string ModelCacheDirectory { get; }

    public string ModelCacheIndexPath { get; }
}
