using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.Jobs.Errors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

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
        services.TryAddSingleton<JobProgressTracker>();
        services.TryAddSingleton<IJobProgress>(provider => provider.GetRequiredService<JobProgressTracker>());
        services.TryAddSingleton<IErrorClassifier, ErrorClassifier>();
        services.TryAddSingleton(RetryPolicy.Default);
        services.TryAddSingleton(JobRetention.Default);
        services.TryAddSingleton<IProviderCircuitBreaker>(provider => new ProviderCircuitBreaker(
            provider.GetRequiredService<TimeProvider>(),
            provider.GetService<ILogger<ProviderCircuitBreaker>>(),
            provider.GetService<IAiProviderRegistry>()));
        return services;
    }
}
