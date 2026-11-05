using System.Text.Json;
using System.Text.Json.Serialization;

namespace MediaFileClassifier.UseCases.Rescan;

/// <summary>
/// What a rescan's reconciliation found and did (E02-S04-T06), kept as the job's result for the summary.
/// </summary>
/// <param name="Folders">Folders listed.</param>
/// <param name="Offline">Folders that couldn't be reached and were skipped (D188).</param>
/// <param name="New">New files, imported like any other.</param>
/// <param name="Moved">Moved or renamed files, relinked to their items.</param>
/// <param name="Missing">Files that are gone, whose items were marked Missing now.</param>
/// <param name="Modified">Files whose size or time changed, or that came back, queued for the check.</param>
/// <param name="Unchanged">Files that are the same.</param>
public sealed record RescanCounts(int Folders, int Offline, int New, int Moved, int Missing, int Modified, int Unchanged)
{
    /// <summary>
    /// Writes the counts as the job's result.
    /// </summary>
    /// <returns>JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, RescanJsonContext.Default.RescanCounts);

    /// <summary>
    /// Reads a job's result.
    /// </summary>
    /// <param name="json">The job's result, or <c>null</c>.</param>
    /// <returns>The counts, or <c>null</c> if there are none or they can't be read.</returns>
    public static RescanCounts? FromJson(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, RescanJsonContext.Default.RescanCounts);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

[JsonSerializable(typeof(RescanCounts))]
internal sealed partial class RescanJsonContext : JsonSerializerContext;
