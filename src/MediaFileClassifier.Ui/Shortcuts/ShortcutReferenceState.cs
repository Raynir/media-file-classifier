using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.Shortcuts;

/// <summary>
/// Whether the shortcut reference (E16-S01-T03) is open. Shared by its command, the overlay and the
/// shortcut handler.
/// </summary>
public sealed partial class ShortcutReferenceState : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether the shortcut reference is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }
}
