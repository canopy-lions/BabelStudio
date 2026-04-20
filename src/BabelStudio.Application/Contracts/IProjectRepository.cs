using BabelStudio.Domain.Projects;

namespace BabelStudio.Application.Contracts;

public interface IProjectRepository
{
    Task InitializeAsync(BabelProject project, CancellationToken cancellationToken);

    Task<BabelProject?> GetAsync(CancellationToken cancellationToken);
}
