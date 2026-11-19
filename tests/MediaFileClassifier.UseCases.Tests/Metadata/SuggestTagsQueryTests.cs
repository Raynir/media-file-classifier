using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.UseCases.Metadata.SuggestTags;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Metadata;

/// <summary>
/// Tests for the tag editor's autocompletion query (E07-S02-T02).
/// </summary>
public sealed class SuggestTagsQueryTests : IDisposable
{
    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public SuggestTagsQueryTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Suggest_PassesThePrefixAndLimitOnAndKeepsTheRepositoryOrder()
    {
        var tags = new TagStub([("holiday", 5), ("hobby", 2)]);
        await OpenLibraryAsync(tags);

        var result = await Sender.Send(new SuggestTagsQuery("  ho ", 5), Token);

        Assert.Equal([new TagSuggestionItem("holiday", 5), new TagSuggestionItem("hobby", 2)], result);
        Assert.Equal(("ho", 5), (tags.Prefix, tags.Limit));
    }

    [Fact]
    public async Task Suggest_WithoutALibraryOrWithNoLimit_IsEmpty()
    {
        Assert.Empty(await Sender.Send(new SuggestTagsQuery("a"), Token));

        await OpenLibraryAsync(new TagStub([("a", 1)]));

        Assert.Empty(await Sender.Send(new SuggestTagsQuery("a", 0), Token));
    }

    private async Task OpenLibraryAsync(IItemMetadataRepository metadata)
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "tags"), "tags") { MetadataRepository = metadata };
        await session.SetCurrentAsync(library);
    }

    private sealed class TagStub((string Text, int Count)[] known) : IItemMetadataRepository
    {
        public string? Prefix { get; private set; }

        public int Limit { get; private set; }

        public Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetEffectiveTagsAsync(string? prefix, int limit, CancellationToken cancellationToken = default)
        {
            Prefix = prefix;
            Limit = limit;
            return Task.FromResult<IReadOnlyList<(TagName Tag, int ItemCount)>>([.. known.Select(tag => (TagName.From(tag.Text), tag.Count))]);
        }

        public Task<ItemMetadata> GetAsync(Id<MediaItem> itemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Id<MediaItem>, ItemMetadata>> GetManyAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(ItemMetadata Metadata, TResult Result)> UpdateAsync<TResult>(Id<MediaItem> itemId, Func<ItemMetadata, TResult> change, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<ItemMetadata> UpdateAsync(Id<MediaItem> itemId, Action<ItemMetadata> change, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Id<MediaItem>>> UpdateManyAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, Func<ItemMetadata, bool> change, Action<IReadOnlyList<Id<MediaItem>>>? written = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task AddTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RemoveTagAsync(IReadOnlyCollection<Id<MediaItem>> itemIds, TagName tag, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<(TagName Tag, int ItemCount)>> GetUserTagsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
