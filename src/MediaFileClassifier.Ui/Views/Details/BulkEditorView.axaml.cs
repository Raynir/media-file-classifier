using Avalonia.Controls;
using Avalonia.VisualTree;

namespace MediaFileClassifier.Ui.Views.Details;

/// <summary>
/// The metadata editor in bulk mode (E07-S03), shown in the details panel while several items are selected.
/// </summary>
public sealed partial class BulkEditorView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BulkEditorView"/> class.
    /// </summary>
    public BulkEditorView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Moves focus to the first field (the title), for the Edit metadata command.
    /// </summary>
    public void FocusFirstField() => this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault()?.Focus();
}
