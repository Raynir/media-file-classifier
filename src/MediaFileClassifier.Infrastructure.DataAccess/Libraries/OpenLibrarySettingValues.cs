using System.Text.Json;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Infrastructure.DataAccess.Libraries;

/// <summary>
/// Library-scope setting values of the open library (E02-S02-T01). Each setting key maps to a typed
/// <see cref="LibrarySettings"/> property, so features keep reading the domain record. A new library setting adds a
/// property there and a mapping here.
/// </summary>
public sealed class OpenLibrarySettingValues : ILibrarySettingValues
{
    private static readonly Dictionary<string, Mapping> Mappings = new(StringComparer.Ordinal)
    {
        [KnownSettings.AnalyzeAutomatically.Key] = new(
            settings => JsonSerializer.SerializeToElement(settings.AnalyzeAutomatically),
            (settings, json) => settings with { AnalyzeAutomatically = json.GetBoolean() }),
        [KnownSettings.RetainedPreviousRuns.Key] = new(
            settings => JsonSerializer.SerializeToElement(settings.RetainedPreviousRuns),
            (settings, json) => settings with { RetainedPreviousRuns = json.GetInt32() }),
    };

    private readonly ILibrarySession session;
    private readonly TimeProvider timeProvider;
    private readonly Lock syncRoot = new();
    private Library? library;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibrarySettingValues"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    /// <param name="timeProvider">The clock.</param>
    public OpenLibrarySettingValues(ILibrarySession session, TimeProvider timeProvider)
    {
        this.session = session;
        this.timeProvider = timeProvider;
        library = session.Current?.Library;
        session.CurrentChanged += (_, _) =>
        {
            lock (syncRoot)
            {
                library = session.Current?.Library;
            }

            AvailabilityChanged?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <inheritdoc />
    public event EventHandler? AvailabilityChanged;

    /// <inheritdoc />
    public bool IsAvailable => session.Current is not null;

    /// <inheritdoc />
    public bool TryGet(string key, out JsonElement value)
    {
        lock (syncRoot)
        {
            if (library is not null && Mappings.TryGetValue(key, out var mapping))
            {
                value = mapping.Read(library.Settings);
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <inheritdoc />
    public void Apply(IReadOnlyDictionary<string, JsonElement?> changes)
    {
        var open = session.Current ?? throw new InvalidOperationException("No library is open.");
        Library changed;
        lock (syncRoot)
        {
            changed = library ?? open.Library;
            var settings = changed.Settings;
            foreach (var (key, json) in changes)
            {
                if (!Mappings.TryGetValue(key, out var mapping))
                {
                    throw new ArgumentException($"{key} isn't a library setting.", nameof(changes));
                }

                // A removed value means the default.
                settings = mapping.Write(settings, json ?? mapping.Read(new LibrarySettings()));
            }

            changed.ChangeSettings(settings, timeProvider.GetUtcNow());
            library = changed;
        }

        // Settings are saved as they change; the write is small and queued behind other writes.
        Task.Run(() => open.UpdateLibraryAsync(changed)).GetAwaiter().GetResult();
    }

    private sealed record Mapping(Func<LibrarySettings, JsonElement> Read, Func<LibrarySettings, JsonElement, LibrarySettings> Write);
}
