using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Monitoring;

/// <summary>
/// Published when folders that were Offline are Online again (E04-S04-T02), e.g. their drive was plugged back in or came
/// back under another drive letter, so what changed on them while they were away is caught up. Their watchers restart by
/// themselves, through the folder repository's <c>StatusChanged</c>.
/// </summary>
/// <param name="FolderIds">The folders that came back.</param>
public sealed record FoldersReconnected(IReadOnlyList<Id<IndexedFolder>> FolderIds) : INotification;
