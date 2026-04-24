using Microsoft.Extensions.DependencyInjection;

namespace BabelStudio.Composition;

public static class CompositionRoot
{
    public static IServiceCollection AddBabelStudio(this IServiceCollection services)
    {
        // Infrastructure registrations will go here
        // Media registrations will go here
        // Inference registrations will go here
        return services;
    }
}
