using Avalonia;
using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// Shows a <see cref="ViewModels.Browser.RemoveItemsConfirmationViewModel"/>.
/// </summary>
public partial class RemoveItemsConfirmationView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RemoveItemsConfirmationView"/> class.
    /// </summary>
    public RemoveItemsConfirmationView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        CancelButton.Focus();
    }
}
