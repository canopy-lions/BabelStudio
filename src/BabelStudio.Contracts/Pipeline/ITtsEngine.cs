namespace BabelStudio.Contracts.Pipeline;

public interface ITtsEngine
{
    Task<TtsSynthesisResult> SynthesizeAsync(
        TtsSynthesisRequest request,
        CancellationToken cancellationToken);
}
