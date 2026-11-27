using System.Collections.ObjectModel;
using Avalonia.Media;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// One row of the duplicate group list (E12-S03-T01): the type, the number of files and the wasted space.
/// </summary>
public sealed class DuplicateGroupRowViewModel : ViewModelBase, IDisposable
{
    private readonly Func<Id<DuplicateGroup>, CancellationToken, Task<IReadOnlyList<ThumbnailLease>>>? stripSource;
    private readonly List<ThumbnailLease> leases = [];
    private CancellationTokenSource? loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateGroupRowViewModel"/> class.
    /// </summary>
    /// <param name="summary">The group.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="stripSource">Loads the strip's thumbnails, or <see langword="null"/> for a row without a strip.</param>
    public DuplicateGroupRowViewModel(
        DuplicateGroupSummary summary,
        Localizer localizer,
        Func<Id<DuplicateGroup>, CancellationToken, Task<IReadOnlyList<ThumbnailLease>>>? stripSource = null)
    {
        this.stripSource = stripSource;
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(localizer);

        Summary = summary;
        TypeText = localizer.Get(summary.Type == DuplicateGroupType.Exact ? "Duplicates_Type_Exact" : "Duplicates_Type_Similar");
        FilesText = localizer.Format("Duplicates_Files", summary.MemberCount);
        WastedText = localizer.Format("Duplicates_Wasted", Format.FileSize(summary.WastedBytes));
        AccessibleName = $"{TypeText}, {FilesText}, {WastedText}";
    }

    /// <summary>
    /// Gets the thumbnails of the group's first members, loaded when the row is shown.
    /// </summary>
    public ObservableCollection<IImage> Strip { get; } = [];

    /// <summary>
    /// Gets the group.
    /// </summary>
    public DuplicateGroupSummary Summary { get; }

    /// <summary>
    /// Gets the type badge text, e.g. <c>Exact</c>.
    /// </summary>
    public string TypeText { get; }

    /// <summary>
    /// Gets the member count text, e.g. <c>3 files</c>.
    /// </summary>
    public string FilesText { get; }

    /// <summary>
    /// Gets the wasted space text, e.g. <c>82 MB wasted</c>.
    /// </summary>
    public string WastedText { get; }

    /// <summary>
    /// Gets the row's name for screen readers.
    /// </summary>
    public string AccessibleName { get; }

    /// <summary>
    /// Loads the strip. Call it when the row is shown; a row that scrolled away calls <see cref="Unload"/>.
    /// </summary>
    /// <returns>A task that completes when the thumbnails are in.</returns>
    public async Task LoadStripAsync()
    {
        if (stripSource is null || loading is not null)
        {
            return;
        }

        loading = new CancellationTokenSource();
        var token = loading.Token;
        var found = await stripSource(Summary.Id, token).ConfigureAwait(true);
        if (token.IsCancellationRequested)
        {
            foreach (var lease in found)
            {
                lease.Dispose();
            }

            return;
        }

        foreach (var lease in found)
        {
            leases.Add(lease);
            Strip.Add(lease.Bitmap);
        }
    }

    /// <summary>
    /// Lets go of the thumbnails, e.g. when the row scrolled out of view.
    /// </summary>
    public void Unload()
    {
        loading?.Cancel();
        loading?.Dispose();
        loading = null;
        Strip.Clear();
        foreach (var lease in leases)
        {
            lease.Dispose();
        }

        leases.Clear();
    }

    /// <inheritdoc />
    public void Dispose() => Unload();
}
