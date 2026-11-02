using MediaFileClassifier.Infrastructure.Abstractions.Matching;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;

namespace MediaFileClassifier.Infrastructure.Matching;

/// <summary>
/// Creates <see cref="FileMatcher"/>s.
/// </summary>
public sealed class FileMatcherFactory : IFileMatcherFactory
{
    /// <inheritdoc />
    public async Task<IFileMatcher> CreateAsync(IMediaItemRepository items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var identities = await items.GetIdentitiesAsync(cancellationToken).ConfigureAwait(false);
        return new FileMatcher(items, identities);
    }
}
