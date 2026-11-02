using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.UseCases.Settings.SetSettingValue;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Settings;

/// <summary>
/// A generated control for a registered setting (E17-S01-T02). Changes are validated and saved at once through
/// <see cref="SetSettingValueCommand"/>; a rejected value shows <see cref="Error"/> and isn't saved.
/// </summary>
public abstract partial class SettingItemViewModel : SettingsEntryViewModel
{
    private readonly ISender sender;
    private bool isLoading;

    private protected SettingItemViewModel(SettingDefinition definition, SettingItemServices services)
        : base(definition.Category, definition.LabelKey, services.Localizer)
    {
        Definition = definition;
        Store = services.Store;
        sender = services.Sender;
    }

    /// <summary>
    /// Gets the setting.
    /// </summary>
    public SettingDefinition Definition { get; }

    /// <summary>
    /// Gets the help text, or <c>null</c> if the setting has none.
    /// </summary>
    public string? Help => Definition.HelpKey is { } key ? Localizer.Get(key) : null;

    /// <summary>
    /// Gets a value indicating whether the setting is stored in the library rather than the app.
    /// </summary>
    public bool IsLibrarySetting => Definition.Scope == SettingScope.Library;

    /// <summary>
    /// Gets the name of the setting's scope.
    /// </summary>
    public string ScopeLabel => Localizer.Get(IsLibrarySetting ? "Settings_ScopeLibrary" : "Settings_ScopeApp");

    /// <summary>
    /// Gets the explanation of the setting's scope.
    /// </summary>
    public string ScopeToolTip => Localizer.Get(IsLibrarySetting ? "Settings_ScopeLibrary_ToolTip" : "Settings_ScopeApp_ToolTip");

    /// <summary>
    /// Gets a note on when a change takes effect, or <c>null</c> when it does at once.
    /// </summary>
    public string? ApplyModeNote => Definition.ApplyMode switch
    {
        SettingApplyMode.RestartRequired => Localizer.Get("Settings_ApplyRestart"),
        SettingApplyMode.ReindexRequired => Localizer.Get("Settings_ApplyReindex"),
        _ => null,
    };

    /// <summary>
    /// Gets a value indicating whether the setting can be changed now. Library settings can't without an open library.
    /// </summary>
    [ObservableProperty]
    public partial bool IsEditable { get; private set; }

    /// <summary>
    /// Gets why the last change was rejected, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    public partial string? Error { get; private set; }

    /// <summary>
    /// Gets the setting values.
    /// </summary>
    private protected ISettingsStore Store { get; }

    /// <summary>
    /// Reloads the value and editability from the store, without saving anything, and clears <see cref="Error"/>.
    /// </summary>
    public void Refresh()
    {
        isLoading = true;
        try
        {
            IsEditable = Store.IsAvailable(Definition.Scope);
            Error = null;
            Load();
        }
        finally
        {
            isLoading = false;
        }
    }

    /// <summary>
    /// Copies the stored value into the control's properties.
    /// </summary>
    private protected abstract void Load();

    /// <summary>
    /// Saves a value the user entered. Does nothing while <see cref="Refresh"/> loads values.
    /// </summary>
    /// <param name="value">The value, of the setting's value type.</param>
    /// <param name="reloadOnError">Whether a rejected value is replaced by the stored one in the control.</param>
    /// <returns>A task that completes when the value is saved or rejected.</returns>
    private protected async Task CommitAsync(object value, bool reloadOnError = true)
    {
        if (isLoading)
        {
            return;
        }

        var result = await sender.Send(new SetSettingValueCommand(Definition, value));
        if (result.Error is { } error)
        {
            if (reloadOnError)
            {
                Refresh();
            }

            ShowError(error);
        }
        else
        {
            Error = null;
        }
    }

    /// <summary>
    /// Shows a validation error without saving anything.
    /// </summary>
    /// <param name="error">The error.</param>
    private protected void ShowError(SettingValidationError error)
        => Error = Localizer.Format(error.MessageKey, error.Arguments);
}
