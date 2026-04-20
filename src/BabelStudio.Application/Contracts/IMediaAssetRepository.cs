using BabelStudio.Domain.Artifacts;
using BabelStudio.Domain.Media;

namespace BabelStudio.Application.Contracts;

public interface IMediaAssetRepository
{
    Task SaveAsync(MediaAsset asset, CancellationToken cancellationToken);

    Task<MediaAsset?> GetPrimaryAsync(Guid projectId, CancellationToken cancellationToken);

    Task SaveArtifactAsync(ProjectArtifact artifact, CancellationToken cancellationToken);

    Task<IReadOnlyList<ProjectArtifact>> GetArtifactsAsync(Guid projectId, CancellationToken cancellationToken);
}
