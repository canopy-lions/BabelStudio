namespace BabelStudio.Domain;

public sealed record SchemaVersionRecord(
    int Version,
    string Name,
    DateTimeOffset AppliedAtUtc);
