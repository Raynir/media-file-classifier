using System.Text.Json;
using System.Text.Json.Nodes;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.UseCases.Search.Filters;

/// <summary>
/// Writes a <see cref="FilterState"/> as JSON and reads it back (E10-S01-T02), for saved searches (E09-S09) and the
/// filter state restored at start (E10-S02-T03). The format is
/// <c>{"version":1,"filters":[{"id":"media-type","value":{"values":["Video"]}}]}</c>; each value is read as its
/// filter's value type. Reading never throws: filters no longer registered and unreadable values are skipped and logged.
/// </summary>
/// <param name="registry">The registered filters.</param>
/// <param name="logger">Logs what was skipped.</param>
public sealed partial class FilterStateSerializer(FilterRegistry registry, ILogger<FilterStateSerializer> logger)
{
    /// <summary>
    /// The format version written.
    /// </summary>
    public const int Version = 1;

    // A value missing one of its properties is unreadable, rather than read with a default that filters differently.
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };

    /// <summary>
    /// Writes a state.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The JSON.</returns>
    public static string Serialize(FilterState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var filters = new JsonArray();
        foreach (var (id, value) in state.Values)
        {
            filters.Add(new JsonObject { ["id"] = id, ["value"] = JsonSerializer.SerializeToNode(value, value.GetType(), Options) });
        }

        return new JsonObject { ["version"] = Version, ["filters"] = filters }.ToJsonString();
    }

    /// <summary>
    /// Reads a state.
    /// </summary>
    /// <param name="json">The JSON, or <see langword="null"/> or blank for no filter.</param>
    /// <returns>The state, without the filters that couldn't be read.</returns>
    public FilterState Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return FilterState.Empty;
        }

        JsonArray? filters;
        try
        {
            var root = JsonNode.Parse(json);
            if (root?["version"] is JsonValue version && version.TryGetValue<int>(out var number) && number > Version)
            {
                LogNewerVersion(number);
                return FilterState.Empty;
            }

            filters = root?["filters"] as JsonArray;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            LogUnreadable(exception);
            return FilterState.Empty;
        }

        var state = FilterState.Empty;
        foreach (var entry in filters ?? [])
        {
            var id = entry is JsonObject item && item["id"] is JsonValue idValue && idValue.TryGetValue<string>(out var text) ? text : null;
            if (string.IsNullOrWhiteSpace(id) || registry.Find(id) is not { } filter)
            {
                LogUnknownFilter(id);
                continue;
            }

            try
            {
                state = state.Set(id, entry!["value"]?.Deserialize(filter.ValueType, Options) as FilterValue);
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
            {
                LogUnreadableValue(id, exception);
            }
        }

        return state;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved filter state isn't valid JSON; no filter is restored")]
    private partial void LogUnreadable(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The saved filter state has format version {FormatVersion}, newer than this app reads; no filter is restored")]
    private partial void LogNewerVersion(int formatVersion);

    [LoggerMessage(Level = LogLevel.Information, Message = "Skipped the saved filter {FilterId}: no such filter is registered")]
    private partial void LogUnknownFilter(string? filterId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipped the saved filter {FilterId}: its value can't be read")]
    private partial void LogUnreadableValue(string filterId, Exception exception);
}
