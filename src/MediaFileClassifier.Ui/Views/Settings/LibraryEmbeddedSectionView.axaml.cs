using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using MediaFileClassifier.Ui.ViewModels.Settings;

namespace MediaFileClassifier.Ui.Views.Settings;

/// <summary>
/// An existing library view inside the Settings "Library" page. It loads the view while it's on screen.
/// </summary>
public partial class LibraryEmbeddedSectionView : UserControl
{
    private readonly DispatcherTimer timer = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryEmbeddedSectionView"/> class.
    /// </summary>
    public LibraryEmbeddedSectionView()
    {
        InitializeComponent();
        timer.Tick += async (_, _) => await RefreshAsync();
    }

    /// <inheritdoc />
    protected override async void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is LibrarySectionViewModel section)
        {
            if (section.RefreshInterval is { } interval)
            {
                timer.Interval = interval;
                timer.Start();
            }

            await SectionLifetime.AttachAsync(section);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        timer.Stop();
        (DataContext as LibrarySectionViewModel)?.Detach();
        base.OnDetachedFromVisualTree(e);
    }

    private async Task RefreshAsync()
    {
        if (DataContext is LibrarySectionViewModel section)
        {
            await SectionLifetime.RefreshAsync(section);
        }
    }
}
