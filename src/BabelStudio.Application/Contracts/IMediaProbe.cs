using BabelStudio.Domain.Media;

namespace BabelStudio.Application.Contracts;

public interface IMediaProbe
{
    Task<MediaProbeSnapshot> ProbeAsync(string sourcePath, CancellationToken cancellationToken);
}
