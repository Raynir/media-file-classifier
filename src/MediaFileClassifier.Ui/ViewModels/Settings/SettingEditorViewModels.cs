using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A <see cref="BooleanSetting"/>, shown as a toggle switch.
/// </summary>
public sealed partial class BooleanSettingItemViewModel : SettingItemViewModel
{
    private readonly BooleanSetting setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="BooleanSettingItemViewModel"/> class.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="services">Shared services.</param>
    public BooleanSettingItemViewModel(BooleanSetting setting, SettingItemServices services)
        : base(setting, services)
    {
        this.setting = setting;
        Refresh();
    }

    /// <summary>
    /// Gets or sets the value. Setting it saves the change.
    /// </summary>
    [ObservableProperty]
    public partial bool Value { get; set; }

    private protected override void Load() => Value = Store.Get(setting);

    partial void OnValueChanged(bool value) => _ = CommitAsync(value);
}

/// <summary>
/// An <see cref="IntegerSetting"/>, shown as a number box.
/// </summary>
public sealed partial class IntegerSettingItemViewModel : SettingItemViewModel
{
    private readonly IntegerSetting setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="IntegerSettingItemViewModel"/> class.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="services">Shared services.</param>
    public IntegerSettingItemViewModel(IntegerSetting setting, SettingItemServices services)
        : base(setting, services)
    {
        this.setting = setting;
        Refresh();
    }

    /// <summary>
    /// Gets the smallest allowed value.
    /// </summary>
    public decimal Minimum => setting.Minimum;

    /// <summary>
    /// Gets the largest allowed value.
    /// </summary>
    public decimal Maximum => setting.Maximum;

    /// <summary>
    /// Gets or sets the value, as the number box's type. Setting a whole number in range saves it.
    /// </summary>
    [ObservableProperty]
    public partial decimal? Value { get; set; }

    private protected override void Load() => Value = Store.Get(setting);

    partial void OnValueChanged(decimal? value)
    {
        if (value is { } number && number == decimal.Truncate(number) && number >= int.MinValue && number <= int.MaxValue)
        {
            _ = CommitAsync((int)number);
        }
        else
        {
            ShowError(new SettingValidationError("SettingError_Range", setting.Minimum, setting.Maximum));
        }
    }
}

/// <summary>
/// A <see cref="TextSetting"/>, shown as a text box. The text is saved when the box loses focus or on Enter.
/// </summary>
public sealed partial class TextSettingItemViewModel : SettingItemViewModel
{
    private readonly TextSetting setting;

    /// <summary>
    /// Initializes a new instance of the <see cref="TextSettingItemViewModel"/> class.
    /// </summary>
    /// <param name="setting">The setting.</param>
    /// <param name="services">Shared services.</param>
    public TextSettingItemViewModel(TextSetting setting, SettingItemServices services)
        : base(setting, services)
    {
        this.setting = setting;
        Refresh();
    }

    /// <summary>
    /// Gets the text shown in the empty box.
    /// </summary>
    public string? Placeholder => setting.PlaceholderKey is { } key ? Localizer.Get(key) : null;

    /// <summary>
    /// Gets or sets the text being edited.
    /// </summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    private protected override void Load() => Text = Store.Get(setting);

    /// <summary>
    /// Saves the edited text, trimmed. Invalid text stays in the box with an error, so it can be corrected.
    /// </summary>
    /// <returns>A task that completes when the text is saved or rejected.</returns>
    [RelayCommand]
    private Task CommitAsync() => CommitAsync(Text.Trim(), reloadOnError: false);
}

/// <summary>
/// A <see cref="ChoiceSetting{TEnum}"/>, shown as a drop-down list.
/// </summary>
public sealed partial class ChoiceSettingItemViewModel : SettingItemViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChoiceSettingItemViewModel"/> class.
    /// </summary>
    /// <param name="setting">The setting, a <see cref="ChoiceSetting{TEnum}"/>.</param>
    /// <param name="services">Shared services.</param>
    public ChoiceSettingItemViewModel(SettingDefinition setting, SettingItemServices services)
        : base(setting, services)
    {
        var choice = (IChoiceSetting)setting;
        Options = [.. choice.Options.Select(option => new ChoiceOptionViewModel(option, choice.OptionLabelKey(option), services.Localizer))];
        Refresh();
    }

    /// <summary>
    /// Gets the options, in order.
    /// </summary>
    public IReadOnlyList<ChoiceOptionViewModel> Options { get; }

    /// <summary>
    /// Gets or sets the selected option. Setting it saves the change.
    /// </summary>
    [ObservableProperty]
    public partial ChoiceOptionViewModel? Selected { get; set; }

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();
        foreach (var option in Options)
        {
            option.RefreshTexts();
        }
    }

    private protected override void Load()
    {
        var value = Store.GetValue(Definition);
        Selected = Options.FirstOrDefault(option => Equals(option.Value, value));
    }

    partial void OnSelectedChanged(ChoiceOptionViewModel? value)
    {
        if (value is not null)
        {
            _ = CommitAsync(value.Value);
        }
    }
}

/// <summary>
/// One option of a <see cref="ChoiceSettingItemViewModel"/>.
/// </summary>
/// <param name="value">The enum member, boxed.</param>
/// <param name="labelKey">Resource key of the label.</param>
/// <param name="localizer">UI strings.</param>
public sealed class ChoiceOptionViewModel(object value, string labelKey, Localizer localizer) : ViewModelBase
{
    /// <summary>
    /// Gets the enum member, boxed.
    /// </summary>
    public object Value { get; } = value;

    /// <summary>
    /// Gets the label.
    /// </summary>
    public string Label => localizer.Get(labelKey);

    /// <summary>
    /// Updates the label after a culture or pseudo-localisation switch.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(nameof(Label));
}
