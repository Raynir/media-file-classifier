using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using MediaFileClassifier.Infrastructure.DataAccess.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests for <see cref="KeyframeRepository"/> against a real library database (E06-S02-T01).
/// </summary>
public sealed class KeyframeRepositoryTests : IAsyncLifetime
{
    private LibraryTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await LibraryTestBed.CreateAsync();

    public ValueTask DisposeAsync() => bed.DisposeAsync();

    [Fact]
    public async Task ReplaceAsync_ThenGetForItemAsync_ReturnsKeyframesInIndexOrder()
    {
        var item = await AddItemAsync(1);
        var later = new Keyframe(Id<Keyframe>.New(), item.Id, 1, TimeSpan.FromSeconds(7.5), 225, 0xF0F0_F0F0_F0F0_F0F0);
        var first = new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.FromSeconds(2.5), null, null);

        await bed.Library.Keyframes.ReplaceAsync(item.Id, [later, first], Token);
        var loaded = await bed.Library.Keyframes.GetForItemAsync(item.Id, Token);

        Assert.Equal([first, later], loaded);
    }

    [Fact]
    public async Task ReplaceAsync_DeletesOldKeyframesAndTheirEmbeddings_KeepsOtherItems()
    {
        var item = await AddItemAsync(1);
        var other = await AddItemAsync(2);
        var old = new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.Zero, 0);
        var kept = new Keyframe(Id<Keyframe>.New(), other.Id, 0, TimeSpan.Zero, 0);
        await bed.Library.Keyframes.ReplaceAsync(item.Id, [old], Token);
        await bed.Library.Keyframes.ReplaceAsync(other.Id, [kept], Token);
        await AddEmbeddingAsync(item.Id, old.Id);
        var replacement = new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.FromSeconds(1), 30);

        await bed.Library.Keyframes.ReplaceAsync(item.Id, [replacement], Token);

        Assert.Equal([replacement], await bed.Library.Keyframes.GetForItemAsync(item.Id, Token));
        Assert.Equal([kept], await bed.Library.Keyframes.GetForItemAsync(other.Id, Token));
        await using var db = await CreateContextAsync();
        Assert.Empty(await db.Embeddings.ToListAsync(Token));
    }

    [Fact]
    public async Task ReplaceAsync_Empty_RemovesAll()
    {
        var item = await AddItemAsync(1);
        await bed.Library.Keyframes.ReplaceAsync(item.Id, [new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.Zero, 0)], Token);

        await bed.Library.Keyframes.ReplaceAsync(item.Id, [], Token);

        Assert.Empty(await bed.Library.Keyframes.GetForItemAsync(item.Id, Token));
    }

    [Fact]
    public async Task ReplaceAsync_KeyframeOfAnotherItem_Throws()
    {
        var item = await AddItemAsync(1);
        var stray = new Keyframe(Id<Keyframe>.New(), Id<MediaItem>.New(), 0, TimeSpan.Zero, 0);

        await Assert.ThrowsAsync<ArgumentException>(() => bed.Library.Keyframes.ReplaceAsync(item.Id, [stray], Token));
    }

    [Fact]
    public async Task ReplaceAsync_DuplicateIndex_Throws()
    {
        var item = await AddItemAsync(1);
        var keyframes = new[]
        {
            new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.Zero, 0),
            new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.FromSeconds(1), 30),
        };

        await Assert.ThrowsAsync<ArgumentException>(() => bed.Library.Keyframes.ReplaceAsync(item.Id, keyframes, Token));
    }

    private async Task<MediaItem> AddItemAsync(int number)
    {
        var item = bed.NewItem(number);
        await bed.Library.Items.AddAsync(item, Token);
        return item;
    }

    private async Task AddEmbeddingAsync(Id<MediaItem> itemId, Id<Keyframe> keyframeId)
    {
        await using var db = await CreateContextAsync();
        db.Embeddings.Add(Embedding.Create(itemId, bed.Model, EmbeddingKind.Image, [1f, 0f, 0f, 0f], LibraryTestBed.Start, keyframeId));
        await db.SaveChangesAsync(Token);
    }

    private Task<LibraryDbContext> CreateContextAsync() =>
        new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(tracking: true, Token);
}
