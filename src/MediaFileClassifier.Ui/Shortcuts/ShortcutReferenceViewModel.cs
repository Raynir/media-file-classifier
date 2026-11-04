using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Platform;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// The shortcut reference (E16-S01-T03): every active shortcut, grouped by scope, with keys labelled as in the
/// current keyboard layout. The list is rebuilt each time it opens, so it follows the layout and the language.
/// </summary>
public sealed partial class ShortcutReferenceViewModel : ViewModelBase
{
    private readonly Keymap keymap;
    private readonly CommandRegistry commands;
    private readonly IKeyboardLayout layout;
    private readonly Localizer localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShortcutReferenceViewModel"/> class.
    /// </summary>
    /// <param name="state">Whether the reference is open.</param>
    /// <param name="keymap">The keymap.</param>
    /// <param name="commands">The command registry, for labels.</param>
    /// <param name="layout">The current keyboard layout.</param>
    /// <param name="localizer">UI strings.</param>
    public ShortcutReferenceViewModel(
        ShortcutReferenceState state, Keymap keymap, CommandRegistry commands, IKeyboardLayout layout, Localizer localizer)
    {
        State = state;
        this.keymap = keymap;
        this.commands = commands;
        this.layout = layout;
        this.localizer = localizer;
        state.PropertyChanged += (_, _) =>
        {
            if (state.IsOpen)
            {
                Refresh();
            }
        };
    }

    /// <summary>
    /// Gets whether the reference is open.
    /// </summary>
    public ShortcutReferenceState State { get; }

    /// <summary>
    /// Gets the shortcuts, one group per scope that has any, in <see cref="ShortcutScopes.All"/> order.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ShortcutGroup> Groups { get; private set; } = [];

    /// <summary>
    /// Rebuilds the list from the keymap.
    /// </summary>
    public void Refresh()
    {
        Groups = [.. ShortcutScopes.All
            .Select(scope => new ShortcutGroup(
                localizer.Get(ShortcutScopes.NameKey(scope)),
                [.. keymap.Bindings
                    .Where(binding => binding.Scope == scope && commands[binding.CommandId].IsVisible)
                    .GroupBy(binding => binding.CommandId)
                    .Select(group => new ShortcutRow(
                        commands[group.Key].PlainLabel,
                        string.Join(", ", group.Select(binding => ShortcutText.Format(binding.Chord, layout)))))]))
            .Where(group => group.Rows.Count > 0)];
    }

    [RelayCommand]
    private void Close() => State.IsOpen = false;
}

/// <summary>
/// The shortcuts of one scope.
/// </summary>
/// <param name="Name">The scope's name.</param>
/// <param name="Rows">One row per command.</param>
public sealed record ShortcutGroup(string Name, IReadOnlyList<ShortcutRow> Rows);

/// <summary>
/// One command and its keys.
/// </summary>
/// <param name="Label">The command's label.</param>
/// <param name="Keys">Its shortcuts, e.g. <c>F1, Shift+/</c>.</param>
public sealed record ShortcutRow(string Label, string Keys);
