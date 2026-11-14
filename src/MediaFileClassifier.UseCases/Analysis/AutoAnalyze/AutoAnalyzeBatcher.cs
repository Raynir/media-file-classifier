using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Analysis.AnalyzeItems;
using MediatR;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Analysis.AutoAnalyze;

/// <summary>
/// Queues the analysis of newly imported items while "analyze automatically" is on (E06-S06-T01). Every registered
/// item lands here; the items of one burst, such as an import, are collected for <see cref="Delay"/> (or until
/// <see cref="MaxBatch"/>) and queued as one job, so an import of thousands of files doesn't create thousands of jobs.
/// </summary>
/// <remarks>
/// Items still waiting when the library closes, or when the setting is switched off, are dropped. They stay Pending,
/// so "analyze pending items" and the prompt when the setting is switched on pick them up.
/// </remarks>
public sealed partial class AutoAnalyzeBatcher : IDisposable
{
    /// <summary>
    /// The most items one batch holds before it is queued without waiting.
    /// </summary>
    public const int MaxBatch = 500;

    /// <summary>
    /// How long a batch collects items after its first one.
    /// </summary>
    public static readonly TimeSpan Delay = TimeSpan.FromSeconds(2);

    private readonly ISettingsStore settings;
    private readonly ILibrarySession session;
    private readonly ISender sender;
    private readonly TimeProvider clock;
    private readonly ILogger<AutoAnalyzeBatcher> logger;
    private readonly Lock gate = new();
    private List<Id<MediaItem>> buffer = [];
    private IOpenLibrary? bufferLibrary;
    private ITimer? timer;
    private bool disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AutoAnalyzeBatcher"/> class.
    /// </summary>
    /// <param name="settings">The setting values.</param>
    /// <param name="session">The open library.</param>
    /// <param name="sender">Sends <see cref="AnalyzeItemsCommand"/>.</param>
    /// <param name="clock">The clock the delay runs on.</param>
    /// <param name="logger">Logger.</param>
    public AutoAnalyzeBatcher(ISettingsStore settings, ILibrarySession session, ISender sender, TimeProvider clock, ILogger<AutoAnalyzeBatcher> logger)
    {
        this.settings = settings;
        this.session = session;
        this.sender = sender;
        this.clock = clock;
        this.logger = logger;
        session.CurrentChanged += OnLibraryChanged;
        settings.Changed += OnSettingChanged;
    }

    /// <summary>
    /// Gets how many items wait for the next batch.
    /// </summary>
    public int Waiting
    {
        get
        {
            lock (gate)
            {
                return buffer.Count;
            }
        }
    }

    /// <summary>
    /// Adds a registered item, unless automatic analysis is off.
    /// </summary>
    /// <param name="itemId">The item.</param>
    public void Add(Id<MediaItem> itemId)
    {
        if (!IsOn() || session.Current is not { } library)
        {
            return;
        }

        List<Id<MediaItem>>? full = null;
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            if (!ReferenceEquals(bufferLibrary, library))
            {
                buffer = [];
                bufferLibrary = library;
            }

            buffer.Add(itemId);
            if (buffer.Count >= MaxBatch)
            {
                full = TakeLocked(out _);
            }
            else
            {
                timer ??= clock.CreateTimer(_ => _ = FlushAsync(), null, Delay, Timeout.InfiniteTimeSpan);
            }
        }

        if (full is not null)
        {
            _ = SendAsync(library, full);
        }
    }

    /// <summary>
    /// Queues the waiting items now.
    /// </summary>
    /// <returns>A task that completes when the batch is queued.</returns>
    public Task FlushAsync()
    {
        List<Id<MediaItem>> items;
        IOpenLibrary? library;
        lock (gate)
        {
            items = TakeLocked(out library);
        }

        return library is null ? Task.CompletedTask : SendAsync(library, items);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.CurrentChanged -= OnLibraryChanged;
        settings.Changed -= OnSettingChanged;
        lock (gate)
        {
            disposed = true;
            Discard();
        }
    }

    private bool IsOn() => settings.IsAvailable(SettingScope.Library) && settings.Get(KnownSettings.AnalyzeAutomatically);

    private List<Id<MediaItem>> TakeLocked(out IOpenLibrary? library)
    {
        var items = buffer;
        library = bufferLibrary;
        buffer = [];
        timer?.Dispose();
        timer = null;
        return items;
    }

    private void Discard()
    {
        buffer = [];
        bufferLibrary = null;
        timer?.Dispose();
        timer = null;
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        lock (gate)
        {
            Discard();
        }
    }

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting.Key == KnownSettings.AnalyzeAutomatically.Key && !IsOn())
        {
            lock (gate)
            {
                Discard();
            }
        }
    }

    private async Task SendAsync(IOpenLibrary library, List<Id<MediaItem>> items)
    {
        // The setting or the library may have changed while the batch collected.
        if (items.Count == 0 || !IsOn() || !ReferenceEquals(session.Current, library))
        {
            return;
        }

        try
        {
            await sender.Send(new AnalyzeItemsCommand(items, Description: items.Count == 1 ? "Analyze 1 new item" : $"Analyze {items.Count} new items")).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogFailed(ex, items.Count);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't queue the automatic analysis of {Count} new items")]
    private partial void LogFailed(Exception exception, int count);
}
