using Avalonia;
using Avalonia.Controls;
using MediaFileClassifier.Ui.ViewModels.Settings;

namespace MediaFileClassifier.Ui.Views.Settings;

/// <summary>
/// The library information inside the Settings "Library" page. It follows the statistics while it's on screen.
/// </summary>
public partial class LibraryInformationSectionView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInformationSectionView"/> class.
    /// </summary>
    public LibraryInformationSectionView()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LibrarySectionViewModel section)
        {
            await SectionLifetime.AttachAsync(section);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        (DataContext as LibrarySectionViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }
}
