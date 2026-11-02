using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediatR;

namespace MediaFileClassifier.UseCases.Settings.SetSettingValue;

/// <summary>
/// Validates and stores a new value of a setting (E17-S01).
/// </summary>
/// <param name="Setting">The setting.</param>
/// <param name="Value">The new value, of the setting's value type.</param>
public sealed record SetSettingValueCommand(SettingDefinition Setting, object Value) : IRequest<SetSettingValueResult>;

/// <summary>
/// Outcome of <see cref="SetSettingValueCommand"/>.
/// </summary>
/// <param name="Error">Why the value was rejected, or <c>null</c> if it was stored.</param>
public sealed record SetSettingValueResult(SettingValidationError? Error)
{
    /// <summary>
    /// Gets the result of a stored value.
    /// </summary>
    public static SetSettingValueResult Stored { get; } = new(Error: null);

    /// <summary>
    /// Gets the result of a library setting changed while no library is open.
    /// </summary>
    public static SetSettingValueResult Unavailable { get; } = new(new SettingValidationError("SettingError_NoLibrary"));

    /// <summary>
    /// Gets the result of a value that couldn't be written to its storage.
    /// </summary>
    public static SetSettingValueResult SaveFailed { get; } = new(new SettingValidationError("SettingError_SaveFailed"));

    /// <summary>
    /// Gets a value indicating whether the value was stored.
    /// </summary>
    public bool Succeeded => Error is null;
}
