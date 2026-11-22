using System.Runtime.CompilerServices;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Tests.DataAccess;
using MediaFileClassifier.Infrastructure.Tests.Search;
using MediaFileClassifier.UseCases;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.Metadata;

/// <summary>
/// A real library with one item, the use cases on top of it, and a recorder of the published metadata changes, for the
/// metadata service's flow tests (E07-S01).
/// </summary>
internal sealed class MetadataTestBed : IAsyncDisposable
{
    private readonly ServiceProvider services;
    private readonly StrongBox<bool> failPublishing;

    private MetadataTestBed(LibraryTestBed library, MediaItem item, ServiceProvider services, List<ItemsMetadataChanged> published, StrongBox<bool> failPublishing)
    {
        Library = library;
        Item = item;
        this.services = services;
        Published = published;
        this.failPublishing = failPublishing;
    }

    public LibraryTestBed Library { get; }

    public MediaItem Item { get; }

    public IOpenLibrary Open => Library.Library;

    public ISender Sender => services.GetRequiredService<ISender>();

    public List<ItemsMetadataChanged> Published { get; }

    public BulkUndoStore Undo => services.GetRequiredService<BulkUndoStore>();

    public ILibrarySession Session => services.GetRequiredService<ILibrarySession>();

    /// <summary>
    /// Gets or sets whether the recording handler throws, as a failing view would.
    /// </summary>
    public bool FailPublishing
    {
        get => failPublishing.Value;
        set => failPublishing.Value = value;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public static async Task<MetadataTestBed> CreateAsync()
    {
        var library = await LibraryTestBed.CreateAsync();
        var item = library.NewItem(1);
        await library.Library.Items.AddAsync(item, Token);
        var published = new List<ItemsMetadataChanged>();
        var failPublishing = new StrongBox<bool>();
        var services = new ServiceCollection()
            .AddSingleton<ILibrarySession>(new TestSession(library.Library))
            .AddSingleton<TimeProvider>(library.Clock)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddUseCases()
            .AddSingleton<INotificationHandler<ItemsMetadataChanged>>(new Recorder(published, failPublishing))
            .BuildServiceProvider();
        return new MetadataTestBed(library, item, services, published, failPublishing);
    }

    public async Task<MediaItem> AddItemAsync(int number)
    {
        var item = Library.NewItem(number);
        await Open.Items.AddAsync(item, Token);
        return item;
    }

    public Task<ItemMetadata> GetAsync(MediaItem? item = null) => Open.Metadata.GetAsync((item ?? Item).Id, Token);

    /// <summary>
    /// Applies a completed run of the "describe" analyzer with a title, an optional description and tags, as re-analysis does.
    /// </summary>
    /// <returns>The run.</returns>
    public async Task<AnalysisRun> ApplyRunAsync(string title, IReadOnlyList<string> tags, MediaItem? item = null, string? description = null)
    {
        var target = item ?? Item;
        var run = AnalysisRun.Start(target.Id, "describe", Library.Model, "p1", Library.Clock.Tick());
        run.Complete(Library.Clock.Tick());
        var now = Library.Clock.GetUtcNow();
        List<AiFieldValue> values = [AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title), 0.9, now)];
        if (description is not null)
        {
            values.Add(AiFieldValue.For(run, DescriptiveField.Description, FieldValue.FromText(description), 0.8, now));
        }

        var assignments = tags.Select(tag => TagAssignment.Ai(run, TagName.From(tag), 0.8, now)).ToList();
        await Open.Metadata.UpdateAsync(target.Id, metadata => metadata.ApplyRun(run, values, assignments, 1), Token);
        return run;
    }

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await Library.DisposeAsync();
    }

    private sealed class Recorder(List<ItemsMetadataChanged> published, StrongBox<bool> fail) : INotificationHandler<ItemsMetadataChanged>
    {
        public Task Handle(ItemsMetadataChanged notification, CancellationToken cancellationToken)
        {
            if (fail.Value)
            {
                throw new InvalidOperationException("handler failed");
            }

            lock (published)
            {
                published.Add(notification);
            }

            return Task.CompletedTask;
        }
    }
}
