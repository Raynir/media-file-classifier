using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Capabilities;

/// <summary>
/// Keeps the capability flags, and fills them from the registered contributors.
/// </summary>
/// <param name="contributors">Contributors run by <see cref="RefreshAsync"/>.</param>
/// <param name="logger">Logger.</param>
public sealed partial class CapabilityService(
    IEnumerable<ICapabilityContributor> contributors,
    ILogger<CapabilityService> logger) : ICapabilityService
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, CapabilityFlag> flags = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public event EventHandler<CapabilityChangedEventArgs>? FlagChanged;

    /// <inheritdoc />
    public IReadOnlyList<CapabilityFlag> Flags
    {
        get
        {
            lock (gate)
            {
                return [.. flags.Values];
            }
        }
    }

    /// <inheritdoc />
    public CapabilityFlag Get(CapabilityDefinition capability)
    {
        lock (gate)
        {
            if (!flags.TryGetValue(capability.Id, out var flag))
            {
                flag = new CapabilityFlag(capability);
                flags.Add(capability.Id, flag);
            }

            return flag;
        }
    }

    /// <inheritdoc />
    public void SetAvailable(CapabilityDefinition capability)
        => Set(capability, flag => flag.MarkAvailable());

    /// <inheritdoc />
    public void SetUnavailable(CapabilityDefinition capability, CapabilityReason reason)
        => Set(capability, flag => flag.MarkUnavailable(reason));

    /// <inheritdoc />
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        foreach (var contributor in contributors)
        {
            try
            {
                await contributor.ContributeAsync(this, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                LogContributorFailed(ex, contributor.GetType().Name);
            }
        }
    }

    private void Set(CapabilityDefinition capability, Func<CapabilityFlag, bool> update)
    {
        var flag = Get(capability);
        bool changed;
        lock (gate)
        {
            changed = update(flag);
        }

        if (changed)
        {
            LogFlagChanged(capability.Id, flag.IsAvailable, flag.Reason);
            FlagChanged?.Invoke(this, new CapabilityChangedEventArgs(flag));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Capability {CapabilityId}: available={IsAvailable}, reason {Reason}")]
    private partial void LogFlagChanged(string capabilityId, bool isAvailable, CapabilityReason? reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "Capability contributor {Contributor} failed; its flags stay as they were")]
    private partial void LogContributorFailed(Exception exception, string contributor);
}
