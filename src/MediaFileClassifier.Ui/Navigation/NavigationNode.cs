using CommunityToolkit.Mvvm.ComponentModel;

namespace MediaFileClassifier.Ui.Navigation;

/// <summary>
/// A node of the navigation tree: a section or an entry, which can be expanded.
/// </summary>
public abstract partial class NavigationNode : ObservableObject
{
    /// <summary>
    /// Gets or sets a value indicating whether the node's children are shown.
    /// </summary>
    [ObservableProperty]
    public partial bool IsExpanded { get; set; }

    /// <summary>
    /// Called after <see cref="IsExpanded"/> changed.
    /// </summary>
    /// <param name="value">The new value.</param>
    protected virtual void OnExpandedChanged(bool value)
    {
    }

    partial void OnIsExpandedChanged(bool value) => OnExpandedChanged(value);
}
