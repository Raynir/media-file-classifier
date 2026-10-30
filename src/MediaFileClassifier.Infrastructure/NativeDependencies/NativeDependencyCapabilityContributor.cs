using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using MediaFileClassifier.Infrastructure.Abstractions.NativeDependencies;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.NativeDependencies;

/// <summary>
/// Runs every native dependency probe, logs what it found, and disables the features whose dependencies are
/// missing or broken (E01-S08-T03). A feature that needs several dependencies is available only if all of them are.
/// </summary>
/// <param name="probes">Registered probes.</param>
/// <param name="logger">Logger.</param>
public sealed partial class NativeDependencyCapabilityContributor(
    IEnumerable<INativeDependencyProbe> probes,
    ILogger<NativeDependencyCapabilityContributor> logger) : ICapabilityContributor
{
    /// <inheritdoc />
    public async Task ContributeAsync(ICapabilityService capabilities, CancellationToken cancellationToken)
    {
        var results = new Dictionary<CapabilityDefinition, List<NativeDependencyStatus>>();
        foreach (var probe in probes)
        {
            var status = await ProbeSafelyAsync(probe, cancellationToken);
            foreach (var capability in probe.Capabilities)
            {
                if (!results.TryGetValue(capability, out var statuses))
                {
                    statuses = [];
                    results.Add(capability, statuses);
                }

                statuses.Add(status);
            }
        }

        // The UI shows one reason; with several missing dependencies it names the first, and the log lists them all.
        foreach (var (capability, statuses) in results)
        {
            var problem = statuses.FirstOrDefault(status => !status.IsAvailable)?.Problem;
            if (problem is not null)
            {
                capabilities.SetUnavailable(capability, problem);
            }
            else
            {
                capabilities.SetAvailable(capability);
            }
        }
    }

    private async Task<NativeDependencyStatus> ProbeSafelyAsync(INativeDependencyProbe probe, CancellationToken cancellationToken)
    {
        NativeDependencyStatus status;
        try
        {
            status = await probe.ProbeAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogProbeFailed(ex, probe.DependencyName);
            return NativeDependencyStatus.Unavailable(CapabilityReasons.FailedToLoad(probe.DependencyName), detail: ex.Message);
        }

        if (!status.IsAvailable && status.Problem is null)
        {
            status = status with { Problem = CapabilityReasons.FailedToLoad(probe.DependencyName) };
        }

        if (status.IsAvailable)
        {
            LogAvailable(probe.DependencyName, status.Version, status.Location ?? "the default library path", status.Detail);
        }
        else
        {
            LogUnavailable(probe.DependencyName, status.Problem, status.Location, status.Detail);
        }

        return status;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Native dependency {Dependency}: available, version {Version}, at {Location}. {Detail}")]
    private partial void LogAvailable(string dependency, string? version, string location, string? detail);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Native dependency {Dependency}: unavailable, {Problem}, at {Location}. {Detail}")]
    private partial void LogUnavailable(string dependency, CapabilityReason? problem, string? location, string? detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Native dependency {Dependency}: the probe failed")]
    private partial void LogProbeFailed(Exception exception, string dependency);
}
