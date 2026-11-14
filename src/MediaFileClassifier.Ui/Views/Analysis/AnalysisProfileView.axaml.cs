using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Analysis;

namespace MediaFileClassifier.Ui.Views.Analysis;

/// <summary>
/// The analysis profile section of the settings.
/// </summary>
public partial class AnalysisProfileView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AnalysisProfileView"/> class.
    /// </summary>
    public AnalysisProfileView()
    {
        InitializeComponent();
        AttachedToVisualTree += (_, _) =>
        {
            // Opening the section checks the providers, so a model that went missing shows at once.
            if (DataContext is AnalysisProfileViewModel page)
            {
                _ = page.RefreshAsync(checkHealth: true);
            }
        };
    }
}
