namespace BabelStudio.Inference.Runtime.ModelManifest;

public sealed class ModelManifestValidationException : InvalidOperationException
{
    public ModelManifestValidationException(string message)
        : base(message)
    {
    }
}
