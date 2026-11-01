namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// Why a value isn't valid for a setting.
/// </summary>
/// <param name="MessageKey">Resource key of the message, a composite format string.</param>
/// <param name="Arguments">Values for the message's placeholders.</param>
public sealed record SettingValidationError(string MessageKey, params object[] Arguments);
