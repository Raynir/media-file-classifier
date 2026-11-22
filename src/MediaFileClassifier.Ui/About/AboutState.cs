using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.About;

/// <summary>
/// Whether the About overlay (MFC-1023) is open. Shared by its command and the overlay.
/// </summary>
public sealed partial class AboutState : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether the About overlay is open.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOpen { get; set; }
}
