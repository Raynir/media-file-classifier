using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.Jobs;

/// <summary>
/// Registers the job system (E05-S01).
/// </summary>
public static class JobsModule
{
    /// <summary>
    /// Adds the job system. Stage handlers are registered by their own modules as <see cref="IStageHandler"/>.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddJobs(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IJobSystem, JobSystem>();
        return services;
    }
}
