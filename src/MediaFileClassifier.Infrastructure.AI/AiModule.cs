using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.AI;

/// <summary>
/// Registers the AI provider infrastructure (E01-S05).
/// </summary>
public static class AiModule
{
    /// <summary>
    /// Adds the provider registry, the model store and the provider-kind helpers. Concrete providers (E06) register
    /// themselves as <see cref="IAiProvider"/>, or are added to the registry at run time.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OnnxSessionFactory>();
        services.TryAddSingleton<IAiProviderRegistry, AiProviderRegistry>();
        services.TryAddSingleton<IModelStore, ModelStore>();
        return services;
    }
}
