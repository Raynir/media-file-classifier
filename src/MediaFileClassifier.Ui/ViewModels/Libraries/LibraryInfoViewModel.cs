using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The library information view (E02-S05-T02): the open library's item counts by media type and status, and its
/// sizes, refreshed while items are imported and analyzed. The view is self-contained, so the library information
/// window and the Settings "Library" page (E02-S07) can both show it. Call <see cref="Attach"/> when it's shown and
/// <see cref="Detach"/> when it's hidden, so it follows the statistics only while someone looks.
/// </summary>
public sealed partial class LibraryInfoViewModel : ViewModelBase
{
    private readonly LibraryStatisticsMonitor monitor;
    private readonly ILibrarySession session;
    private readonly Localizer localizer;
    private readonly LibraryInfoRow total = new("LibraryInfo_Total");
    private readonly LibraryInfoRow images = new("LibraryInfo_Images");
    private readonly LibraryInfoRow animatedImages = new("LibraryInfo_AnimatedImages");
    private readonly LibraryInfoRow videos = new("LibraryInfo_Videos");
    private readonly LibraryInfoRow indexed = new("LibraryInfo_Indexed");
    private readonly LibraryInfoRow pending = new("LibraryInfo_Pending");
    private readonly LibraryInfoRow errors = new("LibraryInfo_Errors");
    private readonly LibraryInfoRow missing = new("LibraryInfo_Missing");
    private readonly LibraryInfoRow mediaSize = new("LibraryInfo_MediaFiles");
    private int attached;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInfoViewModel"/> class.
    /// </summary>
    /// <param name="monitor">Keeps the statistics up to date.</param>
    /// <param name="session">Holds the open library.</param>
    /// <param name="localizer">UI strings.</param>
    public LibraryInfoViewModel(LibraryStatisticsMonitor monitor, ILibrarySession session, Localizer localizer)
    {
        this.monitor = monitor;
        this.session = session;
        this.localizer = localizer;
        Groups =
        [
            new LibraryInfoGroup("LibraryInfo_Items", [total, images, animatedImages, videos]),
            new LibraryInfoGroup("LibraryInfo_Status", [indexed, pending, errors, missing]),
            new LibraryInfoGroup("LibraryInfo_DiskUsage", [mediaSize]),
        ];
        Show();
    }

    /// <summary>
    /// Gets the groups of rows: Items, Status and Disk usage.
    /// </summary>
    public IReadOnlyList<LibraryInfoGroup> Groups { get; }

    /// <summary>
    /// Gets a value indicating whether a library is open, so there are statistics to show.
    /// </summary>
    [ObservableProperty]
    public partial bool HasLibrary { get; private set; }

    /// <summary>
    /// Gets the open library's name, or an empty string.
    /// </summary>
    [ObservableProperty]
    public partial string LibraryName { get; private set; } = string.Empty;

    /// <summary>
    /// Starts following the statistics; call it when the view is shown. Calls nest with <see cref="Detach"/>.
    /// </summary>
    public void Attach()
    {
        if (attached++ > 0)
        {
            return;
        }

        monitor.Changed += OnStatisticsChanged;
        localizer.Changed += OnLocalizerChanged;
        monitor.Start();
        Show();
    }

    /// <summary>
    /// Stops following the statistics; call it when the view is hidden.
    /// </summary>
    public void Detach()
    {
        if (attached == 0 || --attached > 0)
        {
            return;
        }

        monitor.Changed -= OnStatisticsChanged;
        localizer.Changed -= OnLocalizerChanged;
    }

    private void OnStatisticsChanged(object? sender, EventArgs e) => Show();

    private void OnLocalizerChanged(object? sender, EventArgs e) => Show();

    private void Show()
    {
        foreach (var group in Groups)
        {
            group.Header = localizer.Get(group.HeaderKey);
            foreach (var row in group.Rows)
            {
                row.Label = localizer.Get(row.LabelKey);
            }
        }

        var statistics = monitor.Current;
        HasLibrary = statistics is not null && session.Current is not null;
        LibraryName = session.Current?.Library.Name ?? string.Empty;
        statistics ??= LibraryStatistics.Empty;
        total.Value = Format.Number(statistics.Total);
        images.Value = Format.Number(statistics.Images);
        animatedImages.Value = Format.Number(statistics.AnimatedImages);
        videos.Value = Format.Number(statistics.Videos);
        indexed.Value = Format.Number(statistics.Indexed);
        pending.Value = Format.Number(statistics.Pending);
        errors.Value = Format.Number(statistics.Errors);
        missing.Value = Format.Number(statistics.Missing);
        mediaSize.Value = Format.FileSize(statistics.MediaSizeBytes);
    }
}
