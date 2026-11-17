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
    private readonly Lock syncRoot = new();
    private Library? library;

    /// <summary>
    /// Initializes a new instance of the <see cref="OpenLibrarySettingValues"/> class.
    /// </summary>
    /// <param name="session">Holds the open library.</param>
    public OpenLibrarySettingValues(ILibrarySession session)
    {
        this.session = session;
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
        foreach (var key in changes.Keys)
        {
            if (!Mappings.ContainsKey(key))
            {
                throw new ArgumentException($"{key} isn't a library setting.", nameof(changes));
            }
        }

        lock (syncRoot)
        {
            library = open.Library;
        }

        LibrarySettings Change(LibrarySettings current)
        {
            var settings = current;
            foreach (var (key, json) in changes)
            {
                // A removed value means the default.
                var mapping = Mappings[key];
                settings = mapping.Write(settings, json ?? mapping.Read(new LibrarySettings()));
            }

            return settings;
        }

        // A value of the wrong type fails here, as itself, and not as a storage failure.
        _ = Change(open.Library.Settings);

        // Settings are saved as they change; the write is small and queued behind other writes.
        try
        {
            Task.Run(() => open.UpdateSettingsAsync(Change)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The previous settings are back; report it as a storage failure like the app settings.
            throw new IOException("Couldn't save the library settings.", ex);
        }
    }

    private sealed record Mapping(Func<LibrarySettings, JsonElement> Read, Func<LibrarySettings, JsonElement, LibrarySettings> Write);
}
