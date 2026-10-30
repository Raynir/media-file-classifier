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

        foreach (var (capability, statuses) in results)
        {
            var missing = statuses.Where(status => !status.IsAvailable).ToList();
            if (missing.Count > 0)
            {
                capabilities.SetUnavailable(capability, string.Join(" ", missing.Select(status => status.Detail)));
            }
            else
            {
                capabilities.SetAvailable(capability, string.Join(" ", statuses.Select(status => status.Detail)));
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
            return NativeDependencyStatus.Unavailable($"{probe.DependencyName} failed to load: {ex.Message}");
        }

        if (status.IsAvailable)
        {
            LogAvailable(probe.DependencyName, status.Version, status.Location ?? "the default library path");
        }
        else
        {
            LogUnavailable(probe.DependencyName, status.Detail);
        }

        return status;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Native dependency {Dependency}: available, version {Version}, at {Location}")]
    private partial void LogAvailable(string dependency, string? version, string location);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Native dependency {Dependency}: unavailable. {Detail}")]
    private partial void LogUnavailable(string dependency, string detail);

    [LoggerMessage(Level = LogLevel.Error, Message = "Native dependency {Dependency}: the probe failed")]
    private partial void LogProbeFailed(Exception exception, string dependency);
}
