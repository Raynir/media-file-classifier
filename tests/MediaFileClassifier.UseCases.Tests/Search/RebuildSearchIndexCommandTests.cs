using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Search;
using MediaFileClassifier.UseCases.Search.RebuildSearchIndex;
using MediaFileClassifier.UseCases.Tests.Libraries;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.UseCases.Tests.Search;

/// <summary>
/// Tests for <see cref="RebuildSearchIndexCommandHandler"/> (E01-S04-T06).
/// </summary>
public sealed class RebuildSearchIndexCommandTests
{
    [Fact]
    public async Task Handle_RebuildsTheIndex_PassingProgressOn()
    {
        var session = new FakeLibrarySession();
        await session.SetCurrentAsync(new FakeOpenLibrary(LibraryLayout.For(Path.GetTempPath(), "rebuild"), "rebuild"));
        var index = new RecordingSearchIndex();
        var progress = new Progress<double>();
        var handler = new RebuildSearchIndexCommandHandler(session, index, NullLogger<RebuildSearchIndexCommandHandler>.Instance);

        await handler.Handle(new RebuildSearchIndexCommand(progress), TestContext.Current.CancellationToken);

        Assert.Same(progress, Assert.Single(index.Rebuilds));
    }

    [Fact]
    public async Task Handle_NoLibrary_Throws()
    {
        var index = new RecordingSearchIndex();
        var handler = new RebuildSearchIndexCommandHandler(new FakeLibrarySession(), index, NullLogger<RebuildSearchIndexCommandHandler>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(new RebuildSearchIndexCommand(), TestContext.Current.CancellationToken));
        Assert.Empty(index.Rebuilds);
    }

    private sealed class RecordingSearchIndex : ISearchIndex
    {
        public event EventHandler? VectorStateChanged
        {
            add { }
            remove { }
        }

        public List<IProgress<double>?> Rebuilds { get; } = [];

        public VectorIndexState VectorState => VectorIndexState.Ready;

        public Task<SearchResults> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default) => Task.FromResult(SearchResults.Empty);

        public Task RebuildAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            Rebuilds.Add(progress);
            return Task.CompletedTask;
        }
    }
}
