using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Libraries.Statistics;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Libraries.GetLibraryDiskUsage;
using MediaFileClassifier.UseCases.Libraries.GetLibraryStatistics;
using MediatR;

namespace MediaFileClassifier.Ui.ViewModels.Libraries;

/// <summary>
/// The library information view (E02-S05-T02): the open library's item counts by media type and status, and its
/// sizes, refreshed while items are imported and analyzed. The view is self-contained, so the library information
/// window and the Settings "Library" page (E02-S07) can both show it. Call <see cref="Attach"/> when it's shown and
/// <see cref="Detach"/> when it's hidden, so it follows the statistics only while someone looks.
/// </summary>
public sealed partial class LibraryInfoViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// How often the disk usage is measured again while the view is shown.
    /// </summary>
    public static readonly TimeSpan DiskUsageInterval = TimeSpan.FromSeconds(30);

    private readonly ISender sender;
    private readonly Action<Action> post;
    private readonly ITimer diskTimer;
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
    private readonly LibraryInfoRow openDuplicateGroups = new("LibraryInfo_OpenDuplicateGroups");
    private readonly LibraryInfoRow wastedSpace = new("LibraryInfo_WastedSpace");
    private readonly LibraryInfoRow mediaSize = new("LibraryInfo_MediaFiles");
    private readonly LibraryInfoRow database = new("LibraryInfo_Database");
    private readonly LibraryInfoRow thumbnails = new("LibraryInfo_Thumbnails");
    private readonly LibraryInfoRow backups = new("LibraryInfo_Backups");
    private readonly LibraryInfoRow libraryTotal = new("LibraryInfo_LibraryTotal");
    private LibraryDiskUsage? diskUsage;
    private IOpenLibrary? measuredLibrary;
    private IOpenLibrary? measuringLibrary;
    private int attached;
    private int diskVersion;

    /// <summary>
    /// Initializes a new instance of the <see cref="LibraryInfoViewModel"/> class.
    /// </summary>
    /// <param name="monitor">Keeps the statistics up to date.</param>
    /// <param name="session">Holds the open library.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="sender">Measures the disk usage.</param>
    /// <param name="clock">Times the disk usage measurements.</param>
    /// <param name="post">Runs an action on the UI thread, or <see langword="null"/> for the Avalonia dispatcher.</param>
    public LibraryInfoViewModel(
        LibraryStatisticsMonitor monitor,
        ILibrarySession session,
        Localizer localizer,
        ISender sender,
        TimeProvider clock,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        this.monitor = monitor;
        this.session = session;
        this.localizer = localizer;
        this.sender = sender;
        this.post = post ?? (action => Dispatcher.UIThread.Post(action));
        diskTimer = clock.CreateTimer(_ => this.post(() => _ = MeasureDiskUsageAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Groups =
        [
            new LibraryInfoGroup("LibraryInfo_Items", [total, images, animatedImages, videos]),
            new LibraryInfoGroup("LibraryInfo_Status", [indexed, pending, errors, missing]),
            new LibraryInfoGroup("LibraryInfo_Duplicates", [openDuplicateGroups, wastedSpace]),
            new LibraryInfoGroup("LibraryInfo_DiskUsage", [mediaSize, database, thumbnails, backups, libraryTotal]),
        ];
        Show();
    }

    /// <summary>
    /// Gets the groups of rows: Items, Status, Duplicates and Disk usage.
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
        diskTimer.Change(DiskUsageInterval, DiskUsageInterval);
        _ = MeasureDiskUsageAsync();
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
        diskTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Measures the disk usage now, or takes it from the cache when it's younger than <see cref="DiskUsageInterval"/>.
    /// Call it on the UI thread.
    /// </summary>
    /// <returns>A task that completes when the disk rows are up to date.</returns>
    public async Task MeasureDiskUsageAsync()
    {
        var current = ++diskVersion;
        var library = session.Current;
        measuringLibrary = library;
        LibraryDiskUsage? measured;
        try
        {
            measured = await sender.Send(new GetLibraryDiskUsageQuery(DiskUsageInterval - TimeSpan.FromSeconds(1)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // E.g. the library closed meanwhile: keep "Calculating…" or the last sizes, the next measurement retries.
            return;
        }

        if (current != diskVersion || !ReferenceEquals(library, session.Current))
        {
            return;
        }

        measuredLibrary = library;
        diskUsage = measured;
        Show();
    }

    /// <inheritdoc />
    public void Dispose() => diskTimer.Dispose();

    private void OnStatisticsChanged(object? sender, EventArgs e)
    {
        // Another library opened: its sizes are measured from scratch.
        if (!ReferenceEquals(measuringLibrary, session.Current))
        {
            measuredLibrary = null;
            diskUsage = null;
            _ = MeasureDiskUsageAsync();
        }

        Show();
    }

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
        openDuplicateGroups.Value = Format.Number(monitor.Duplicates.OpenGroups);
        wastedSpace.Value = Format.FileSize(monitor.Duplicates.WastedBytes);
        mediaSize.Value = Format.FileSize(statistics.MediaSizeBytes);
        var calculating = localizer.Get("LibraryInfo_Calculating");
        var usage = ReferenceEquals(measuredLibrary, session.Current) ? diskUsage : null;
        database.Value = usage is null ? calculating : Format.FileSize(usage.DatabaseBytes);
        thumbnails.Value = usage is null ? calculating : Format.FileSize(usage.ThumbnailsBytes);
        backups.Value = usage is null ? calculating : Format.FileSize(usage.BackupsBytes);
        libraryTotal.Value = usage is null ? calculating : Format.FileSize(usage.TotalBytes);
    }
}
