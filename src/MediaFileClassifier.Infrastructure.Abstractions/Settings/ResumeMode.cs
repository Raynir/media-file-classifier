using System.Text.Json.Serialization;

namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// What happens to unfinished work when a library opens (E05-S01-T05).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ResumeMode>))]
public enum ResumeMode
{
    /// <summary>
    /// Processing resumes at once.
    /// </summary>
    Automatic,

    /// <summary>
    /// Processing waits until the user confirms.
    /// </summary>
    Ask,
}
