using MediatR;

namespace MediaFileClassifier.UseCases.Settings.ResetSettings;

/// <summary>
/// Resets the settings of a category, or all settings, to their defaults (E17-S01-T03). Library settings are
/// only reset while a library is open.
/// </summary>
/// <param name="Category">One of <c>SettingCategories</c>, or <c>null</c> for every setting.</param>
public sealed record ResetSettingsCommand(string? Category) : IRequest;
