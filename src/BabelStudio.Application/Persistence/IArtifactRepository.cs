using System.Data.Common;
using BabelStudio.Domain;

namespace BabelStudio.Application.Persistence;

public interface IArtifactRepository
{
    Task RegisterAsync(
        DbConnection connection,
        ArtifactRecord artifact,
        DbTransaction? transaction = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ArtifactRecord>> ListByProjectAsync(
        DbConnection connection,
        Guid projectId,
        CancellationToken cancellationToken = default);
}
