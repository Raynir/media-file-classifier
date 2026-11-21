using Avalonia.Controls;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// The delete-library confirmation (E02-S07-T02). The name box has focus, so the user types the name right away.
/// </summary>
public partial class DeleteLibraryWindow : Window
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeleteLibraryWindow"/> class.
    /// </summary>
    public DeleteLibraryWindow()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        NameBox.Focus();
    }
}
