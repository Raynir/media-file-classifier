using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Commands;

/// <summary>
/// An application command (E13-S05): one ID, label, icon, shortcut, enablement rule and handler, shared by
/// the main menu, context menus, buttons and keyboard shortcuts so they stay consistent.
/// Register commands through an <see cref="ICommandContributor"/>, and bind surfaces to them by ID through
/// <see cref="CommandRegistry"/>.
/// </summary>
public sealed class AppCommand : ObservableObject, ICommand, IMenuEntry
{
    private readonly Func<Task> execute;
    private Localizer? localizer;
    private ICapabilityGate? capabilities;
    private Geometry? icon;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppCommand"/> class.
    /// </summary>
    /// <param name="id">Unique ID, <c>&lt;group&gt;.&lt;name&gt;</c>, e.g. <c>help.open-log-folder</c>.</param>
    /// <param name="group">Main menu group, one of <see cref="CommandGroups"/>.</param>
    /// <param name="order">Position in the group. Commands are listed in ascending order.</param>
    /// <param name="labelKey">Resource key of the label. An underscore marks the access key, e.g. <c>Open _log folder</c>.</param>
    /// <param name="execute">The handler.</param>
    public AppCommand(string id, string group, int order, string labelKey, Func<Task> execute)
    {
        Id = id;
        Group = group;
        Order = order;
        LabelKey = labelKey;
        this.execute = execute;
        Label = labelKey;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppCommand"/> class with a synchronous handler.
    /// </summary>
    /// <param name="id">Unique ID, <c>&lt;group&gt;.&lt;name&gt;</c>.</param>
    /// <param name="group">Main menu group, one of <see cref="CommandGroups"/>.</param>
    /// <param name="order">Position in the group.</param>
    /// <param name="labelKey">Resource key of the label.</param>
    /// <param name="execute">The handler.</param>
    public AppCommand(string id, string group, int order, string labelKey, Action execute)
        : this(id, group, order, labelKey, () =>
        {
            execute();
            return Task.CompletedTask;
        })
    {
    }

    /// <inheritdoc />
    public event EventHandler? CanExecuteChanged;

    /// <summary>
    /// Gets the unique ID.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the main menu group.
    /// </summary>
    public string Group { get; }

    /// <summary>
    /// Gets the position in the group.
    /// </summary>
    public int Order { get; }

    /// <summary>
    /// Gets the resource key of the label.
    /// </summary>
    public string LabelKey { get; }

    /// <summary>
    /// Gets the resource key of the icon in <c>Theming/Icons.axaml</c>, e.g. <c>Icon.FolderOpen</c>.
    /// </summary>
    public string? IconKey { get; init; }

    /// <summary>
    /// Gets the resource key of the submenu header that the command is listed under in the main menu, e.g.
    /// <c>Menu_NavigationPanel</c>, or <c>null</c> to list it in its group directly. The submenu takes the
    /// position of its first command.
    /// </summary>
    public string? Submenu { get; init; }

    /// <summary>
    /// Gets the shortcut shown in menus, or <c>null</c>. The keymap sets it from its first binding for the
    /// command (E16-S01); commands never declare their own.
    /// </summary>
    public KeyGesture? Gesture { get; private set; }

    /// <summary>
    /// Gets the shortcut as tooltips write it, e.g. <c>Ctrl+1</c>, or <c>null</c>.
    /// </summary>
    public string? GestureText { get; private set; }

    /// <summary>
    /// Gets the ID of a capability the command needs (E01-S07-T03). Without it, the command is disabled,
    /// and its tooltip gives the capability's reason.
    /// </summary>
    public string? RequiredCapability { get; init; }

    /// <summary>
    /// Gets a rule that returns why the command can't run in the current context, such as an empty
    /// selection, or <c>null</c> when it can. Call <see cref="Refresh"/> when the context changes.
    /// </summary>
    public Func<string?>? WhyDisabled { get; init; }

    /// <summary>
    /// Gets a rule that returns whether the command is checked, for toggles and choices.
    /// </summary>
    public Func<bool>? IsCheckedRule { get; init; }

    /// <summary>
    /// Gets how the command shows its checked state in menus.
    /// </summary>
    public MenuItemToggleType ToggleType { get; init; }

    /// <summary>
    /// Gets a value indicating whether the command is shown. Use it for commands a platform head doesn't
    /// support at all; use <see cref="RequiredCapability"/> for ones that can become available.
    /// </summary>
    public bool IsVisible { get; init; } = true;

    /// <summary>
    /// Gets the label, with an underscore before the access key.
    /// </summary>
    public string Label { get; private set; }

    /// <summary>
    /// Gets the label without the access-key marker, for tooltips and accessible names.
    /// </summary>
    public string PlainLabel => Label.Replace("_", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Gets the icon geometry, or <c>null</c>.
    /// </summary>
    public Geometry? Icon => icon ??= IconKey is not null
        && Application.Current?.TryGetResource(IconKey, null, out var resource) == true
            ? resource as Geometry
            : null;

    /// <summary>
    /// Gets a value indicating whether the command can run now.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Gets why the command is disabled, or <c>null</c> when it's enabled.
    /// </summary>
    public string? DisabledReason { get; private set; }

    /// <summary>
    /// Gets the tooltip: why the command is disabled, or else its label and shortcut, e.g.
    /// <c>Show or hide navigation panel (Ctrl+1)</c>.
    /// </summary>
    public string ToolTip => DisabledReason ?? (GestureText is null ? PlainLabel : $"{PlainLabel} ({GestureText})");

    /// <summary>
    /// Gets a value indicating whether the command is checked.
    /// </summary>
    public bool IsChecked { get; private set; }

    /// <summary>
    /// Re-evaluates the label, enablement and checked state, and notifies bound surfaces.
    /// </summary>
    public void Refresh()
    {
        Label = localizer?.Get(LabelKey) ?? LabelKey;
        string? reason = null;
        if (RequiredCapability is not null && capabilities?.Check(RequiredCapability) is { IsAvailable: false } state)
        {
            reason = state.Reason;
        }

        reason ??= WhyDisabled?.Invoke();
        var wasEnabled = IsEnabled;
        IsEnabled = reason is null;
        DisabledReason = reason;
        IsChecked = IsCheckedRule?.Invoke() ?? false;

        OnPropertyChanged(string.Empty);
        if (wasEnabled != IsEnabled)
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Runs the command if it's enabled.
    /// </summary>
    /// <returns>A task that completes when the handler has finished.</returns>
    public Task ExecuteAsync() => IsEnabled ? execute() : Task.CompletedTask;

    /// <inheritdoc />
    ICommand? IMenuEntry.Command => this;

    /// <inheritdoc />
    IReadOnlyList<IMenuEntry>? IMenuEntry.Items => null;

    /// <inheritdoc />
    bool ICommand.CanExecute(object? parameter) => IsEnabled;

    /// <inheritdoc />
    async void ICommand.Execute(object? parameter) => await ExecuteAsync();

    /// <summary>
    /// Sets the shortcut shown in menus and tooltips. Called by the keymap.
    /// </summary>
    /// <param name="gesture">The gesture for menus.</param>
    /// <param name="text">The text for tooltips.</param>
    internal void SetShortcut(KeyGesture gesture, string text)
    {
        Gesture = gesture;
        GestureText = text;
        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Connects the command to the services its state depends on. Called by <see cref="CommandRegistry"/>.
    /// </summary>
    internal void Attach(Localizer localizer, ICapabilityGate capabilities)
    {
        this.localizer = localizer;
        this.capabilities = capabilities;
        Refresh();
    }
}
