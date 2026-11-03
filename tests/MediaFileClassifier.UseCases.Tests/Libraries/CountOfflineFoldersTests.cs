using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.UseCases.Libraries.CountOfflineFolders;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Libraries;

/// <summary>
/// Tests for the offline folder count of the status bar (E13-S04-T02).
/// </summary>
public sealed class CountOfflineFoldersTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeLibrarySession session = new();
    private readonly ServiceProvider services;

    public CountOfflineFoldersTests()
    {
        services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(session)
            .AddUseCases()
            .BuildServiceProvider();
    }

    private ISender Sender => services.GetRequiredService<ISender>();

    public void Dispose() => services.Dispose();

    [Fact]
    public async Task Count_WithoutLibrary_IsZero() =>
        Assert.Equal(0, await Sender.Send(new CountOfflineFoldersQuery(), TestContext.Current.CancellationToken));

    [Fact]
    public async Task Count_CountsOnlyTheOfflineFolders()
    {
        var library = new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "offline-count"), "offline-count");
        var online = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Now);
        var offline = IndexedFolder.Create(library.Library.Id, @"E:\Drive", @"E:\Drive", Now);
        offline.MarkOffline(Now);
        library.FolderList.Folders.AddRange([online, offline]);
        await session.SetCurrentAsync(library);

        Assert.Equal(1, await Sender.Send(new CountOfflineFoldersQuery(), TestContext.Current.CancellationToken));

        online.MarkOffline(Now);
        Assert.Equal(2, await Sender.Send(new CountOfflineFoldersQuery(), TestContext.Current.CancellationToken));
    }
}
