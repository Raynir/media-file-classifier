using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Published when the monitoring state of folders may have changed (E04-S05): a folder started or stopped being watched
/// or polled, e.g. its mode changed, its watcher failed or it went Offline. Read the states again with
/// <see cref="GetFolderMonitoring.GetFolderMonitoringQuery"/>.
/// </summary>
public sealed record FolderMonitoringChanged : INotification;
