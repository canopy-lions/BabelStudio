using BabelStudio.Domain;

namespace BabelStudio.Inference;

public interface IModelBenchmarkRunner
{
    Task<BenchmarkReport> RunAsync(BenchmarkRequest request, CancellationToken cancellationToken);
}
