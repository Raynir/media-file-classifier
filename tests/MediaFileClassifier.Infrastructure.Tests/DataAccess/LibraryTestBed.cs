using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.DataAccess.Libraries;
using MediaFileClassifier.Testing.Libraries;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// A real library in the temp folder, created by <see cref="LibraryStore"/>, with one folder and one model.
/// </summary>
internal sealed class LibraryTestBed : IAsyncDisposable
{
    /// <summary>
    /// The clock's start, whole milliseconds, because times are stored as Unix milliseconds.
    /// </summary>
    public static readonly DateTimeOffset Start = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);

    private LibraryTestBed(
        TemporaryLibrary temporary,
        TestClock clock,
        LibraryStore store,
        IOpenLibrary library,
        IndexedFolder folder,
        ModelRegistration model)
    {
        Temporary = temporary;
        Clock = clock;
        Store = store;
        Library = library;
        Folder = folder;
        Model = model;
    }

    public TemporaryLibrary Temporary { get; }

    public LibraryStore Store { get; }

    public TestClock Clock { get; }

    public IOpenLibrary Library { get; private set; }

    public IndexedFolder Folder { get; }

    public ModelRegistration Model { get; }

    /// <summary>
    /// Creates a library named <c>test</c> with a folder and an embedding model.
    /// </summary>
    /// <param name="loggers">Creates the open library's loggers, or <c>null</c> for no logging.</param>
    /// <returns>The test bed. The caller disposes it.</returns>
    public static async Task<LibraryTestBed> CreateAsync(ILoggerFactory? loggers = null)
    {
        var temporary = TemporaryLibrary.Reserve();
        var clock = new TestClock(Start);
        var store = CreateStore(clock, loggers);
        var library = await store.CreateAsync(temporary.ParentDirectory, "test", TestContext.Current.CancellationToken);
        var folder = IndexedFolder.Create(library.Library.Id, @"D:\Photos", @"D:\Photos", Start);
        await library.Folders.AddAsync(folder, TestContext.Current.CancellationToken);
        var model = ModelRegistration.Create("local", "clip", "1", null, 4, Start);
        await library.Models.AddAsync(model, TestContext.Current.CancellationToken);
        return new LibraryTestBed(temporary, clock, store, library, folder, model);
    }

    /// <summary>
    /// Creates a store with a fixed clock and no logging.
    /// </summary>
    /// <param name="clock">Clock.</param>
    /// <param name="loggers">Creates the open libraries' loggers, or <c>null</c> for no logging.</param>
    /// <returns>The store.</returns>
    public static LibraryStore CreateStore(TimeProvider clock, ILoggerFactory? loggers = null) =>
        new(clock, NullLogger<LibraryStore>.Instance, loggers ?? NullLoggerFactory.Instance);

    /// <summary>
    /// Makes a new, unsaved item in the test folder.
    /// </summary>
    /// <param name="number">Number used in the file name.</param>
    /// <returns>The item.</returns>
    public MediaItem NewItem(int number) =>
        MediaItem.Discover(
            Folder.Id,
            RelativePath.From($"album/IMG_{number:D5}.jpg"),
            MediaType.Image,
            1000 + number,
            Start.AddDays(-number),
            Start.AddDays(-number).AddHours(1),
            (UInt128)number * 7919,
            Start);

    /// <summary>
    /// Closes the library and opens it again.
    /// </summary>
    /// <returns>A task that completes when the library is open again.</returns>
    public async Task ReopenAsync()
    {
        await Library.DisposeAsync();
        await OpenAgainAsync();
    }

    /// <summary>
    /// Opens the library after it was closed.
    /// </summary>
    /// <returns>A task that completes when the library is open again.</returns>
    public async Task OpenAgainAsync() =>
        Library = await Store.OpenAsync(Temporary.LibraryDirectory, TestContext.Current.CancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Library.DisposeAsync();
        Temporary.Dispose();
    }
}

/// <summary>
/// A clock that only moves when told to.
/// </summary>
/// <param name="now">Starting time.</param>
internal sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset now = now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => now;

    /// <summary>
    /// Moves the clock one second on.
    /// </summary>
    /// <returns>The new time.</returns>
    public DateTimeOffset Tick() => now = now.AddSeconds(1);
}

/// <summary>
/// A logger that throws on the first message, to make a library operation fail once its database is written.
/// </summary>
/// <typeparam name="T">Category.</typeparam>
internal sealed class ThrowingLogger<T> : ILogger<T>
{
    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Information;

    /// <inheritdoc />
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        throw new InvalidOperationException("Simulated failure.");
}

/// <summary>
/// Keeps the errors logged through it, with their exceptions.
/// </summary>
internal sealed class RecordingLoggerFactory : ILoggerFactory
{
    public List<(string Category, Exception? Exception)> Errors { get; } = [];

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    /// <inheritdoc />
    public void AddProvider(ILoggerProvider provider)
    {
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private sealed class Logger(RecordingLoggerFactory owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
            {
                lock (owner.Errors)
                {
                    owner.Errors.Add((category, exception));
                }
            }
        }
    }
}
