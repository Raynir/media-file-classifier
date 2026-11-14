using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Analysis;

namespace MediaFileClassifier.Ui.Views.Analysis;

/// <summary>
/// The endpoints section of the settings.
/// </summary>
public partial class EndpointsView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointsView"/> class.
    /// </summary>
    public EndpointsView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            // Opening the section lists the endpoints and looks for LM Studio.
            if (DataContext is EndpointsViewModel page)
            {
                _ = page.RefreshAsync();
            }
        };
    }
}
