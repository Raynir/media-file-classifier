using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// The services every <see cref="SettingItemViewModel"/> uses.
/// </summary>
/// <param name="Store">The setting values.</param>
/// <param name="Sender">Sends the change commands.</param>
/// <param name="Localizer">UI strings.</param>
public sealed record SettingItemServices(ISettingsStore Store, ISender Sender, Localizer Localizer);
