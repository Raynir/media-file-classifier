using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases;

/// <summary>
/// Registers the use case layer.
/// </summary>
public static class UseCasesModule
{
    /// <summary>
    /// Adds MediatR with every handler in this assembly (D063).
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddUseCases(this IServiceCollection services)
    {
        services.AddMediatR(config => config.RegisterServicesFromAssembly(typeof(UseCasesModule).Assembly));
        return services;
    }
}
