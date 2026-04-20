namespace BabelStudio.Infrastructure.Persistence.Migrations;

internal sealed record SqliteMigration(
    int Version,
    string Name,
    string Sql);
