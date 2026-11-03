using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediatR;

namespace MediaFileClassifier.UseCases.Import.ImportFolders;

/// <summary>
/// Starts importing folders of the open library in the background (E03-S07): one <see cref="ImportStages.ImportJob"/>
/// job per folder, which begins with <see cref="ImportStages.Discover"/>.
/// </summary>
/// <param name="FolderIds">The folders.</param>
public sealed record ImportFoldersCommand(IReadOnlyList<Id<IndexedFolder>> FolderIds) : IRequest<IReadOnlyList<Id<ProcessingJob>>>;
