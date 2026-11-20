using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Browsing.ItemKeyframes;
using MediaFileClassifier.UseCases.Tests.Libraries;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Browsing;

/// <summary>
/// Tests for <see cref="GetItemKeyframesQuery"/> (E14-S06-T02): the timeline's keyframe markers.
/// </summary>
public sealed class ItemKeyframesTests : IDisposable
{
    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public ItemKeyframesTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Query_ReturnsTheItemsKeyframesInTimeOrder()
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "keyframes"), "keyframes");
        await session.SetCurrentAsync(library);
        var item = Id<MediaItem>.New();
        Keyframe[] keyframes =
        [
            new(Id<Keyframe>.New(), item, 1, TimeSpan.FromSeconds(8), 240),
            new(Id<Keyframe>.New(), item, 0, TimeSpan.FromSeconds(2), 60),
        ];
        library.KeyframeList.Keyframes[item] = keyframes;

        var markers = await Sender.Send(new GetItemKeyframesQuery(item), TestContext.Current.CancellationToken);

        Assert.Equal(
            [new KeyframeMarker(keyframes[1].Id, 0, TimeSpan.FromSeconds(2)), new KeyframeMarker(keyframes[0].Id, 1, TimeSpan.FromSeconds(8))],
            markers);
        Assert.Empty(await Sender.Send(new GetItemKeyframesQuery(Id<MediaItem>.New()), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Query_WithoutALibrary_ReturnsNone()
    {
        Assert.Empty(await Sender.Send(new GetItemKeyframesQuery(Id<MediaItem>.New()), TestContext.Current.CancellationToken));
    }
}
