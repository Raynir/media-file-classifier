using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Libraries;

namespace MediaFileClassifier.Ui.Views.Libraries;

/// <summary>
/// Shows an <see cref="ExcludedItemsViewModel"/>.
/// </summary>
public partial class ExcludedItemsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ExcludedItemsView"/> class.
    /// </summary>
    public ExcludedItemsView()
    {
        InitializeComponent();
    }

    // The list's multiple selection isn't bindable with compiled bindings, so it's passed on here.
    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is ExcludedItemsViewModel viewModel)
        {
            viewModel.SelectedItems = [.. ItemList.SelectedItems?.OfType<ExcludedItemRowViewModel>() ?? []];
        }
    }
}
