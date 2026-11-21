using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaFileClassifier.Ui.ViewModels.Browser;

namespace MediaFileClassifier.Ui.Views.Browser;

/// <summary>
/// The list view's column header (E14-S02).
/// </summary>
public partial class ListHeaderView : UserControl
{
    private ListColumnsViewModel? columns;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListHeaderView"/> class.
    /// </summary>
    public ListHeaderView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        columns = DataContext as ListColumnsViewModel;
        BuildCells();
    }

    private void BuildCells()
    {
        Cells.Children.Clear();
        if (columns is null || Resources["HeaderCellTemplate"] is not IDataTemplate template)
        {
            return;
        }

        foreach (var column in columns.Columns)
        {
            // The data context is set before the cell joins the panel, so its compiled bindings never see the list.
            var cell = template.Build(column) ?? new Control();
            cell.DataContext = column;
            ListCellsPanel.SetColumn(cell, column.Kind);
            Cells.Children.Add(cell);
        }
    }
}
