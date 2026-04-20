namespace BabelStudio.Domain;

public sealed record LocalModelCacheRecord(
    string ModelId,
    string RootPath,
    string Revision,
    string Sha256,
    DateTimeOffset CachedAtUtc);
