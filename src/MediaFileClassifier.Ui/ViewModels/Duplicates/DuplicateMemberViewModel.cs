using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Duplicates.Review;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// One card of the group comparison (E12-S03-T02): a member's preview, its attributes and whether it is the keeper.
/// </summary>
public sealed partial class DuplicateMemberViewModel : ViewModelBase, IDisposable
{
    private ThumbnailLease? lease;

    /// <summary>
    /// Initializes a new instance of the <see cref="DuplicateMemberViewModel"/> class.
    /// </summary>
    /// <param name="detail">The member.</param>
    /// <param name="localizer">UI strings.</param>
    public DuplicateMemberViewModel(DuplicateMemberDetail detail, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(detail);
        ArgumentNullException.ThrowIfNull(localizer);

        Detail = detail;
        StatusText = detail.IsAvailable ? string.Empty : localizer.Get("Duplicates_Missing");
        AccessibleName = $"{detail.FileName}, {detail.Folder}";
    }

    /// <summary>
    /// Gets the member.
    /// </summary>
    public DuplicateMemberDetail Detail { get; }

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Detail.FileName;

    /// <summary>
    /// Gets a value indicating whether the file can't be kept: it is Missing or Excluded, or its folder is offline.
    /// </summary>
    public bool IsUnavailable => !Detail.IsAvailable;

    /// <summary>
    /// Gets the reason the file can't be kept, or an empty text.
    /// </summary>
    public string StatusText { get; }

    /// <summary>
    /// Gets the card's name for screen readers.
    /// </summary>
    public string AccessibleName { get; }

    /// <summary>
    /// Gets the attribute rows of the card, in the same order on every card.
    /// </summary>
    public IReadOnlyList<ComparisonCell> Cells { get; private set; } = [];

    /// <summary>
    /// Gets the preview, or <see langword="null"/> while it loads or when the file has no thumbnail.
    /// </summary>
    [ObservableProperty]
    public partial IImage? Preview { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this is the file to keep.
    /// </summary>
    [ObservableProperty]
    public partial bool IsKeeper { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the arrow keys are on this card.
    /// </summary>
    [ObservableProperty]
    public partial bool IsCurrent { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the keeper rules suggested this file.
    /// </summary>
    [ObservableProperty]
    public partial bool IsSuggested { get; set; }

    /// <summary>
    /// Fills the attribute rows of all cards and marks the rows whose values differ.
    /// </summary>
    /// <param name="members">The cards of one group.</param>
    /// <param name="localizer">UI strings.</param>
    public static void FillCells(IReadOnlyList<DuplicateMemberViewModel> members, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(members);
        ArgumentNullException.ThrowIfNull(localizer);

        var unknown = localizer.Get("Duplicates_Unknown");
        var rows = new (string Label, Func<DuplicateMemberDetail, string> Value)[]
        {
            (localizer.Get("Duplicates_Attr_Resolution"), detail => detail.Width is > 0 && detail.Height is > 0 ? $"{detail.Width}×{detail.Height}" : unknown),
            (localizer.Get("Duplicates_Attr_Size"), detail => Format.FileSize(detail.SizeBytes)),
            (localizer.Get("Duplicates_Attr_Format"), detail => string.IsNullOrEmpty(detail.Format) ? unknown : detail.Format.ToUpperInvariant()),
            (localizer.Get("Duplicates_Attr_Taken"), detail => detail.DateTaken is { } taken ? taken.ToString("d", System.Globalization.CultureInfo.CurrentCulture) : unknown),
            (localizer.Get("Duplicates_Attr_Folder"), detail => string.IsNullOrEmpty(detail.Folder) ? unknown : detail.Folder),
            (localizer.Get("Duplicates_Attr_Metadata"), detail => MetadataText(detail, localizer)),
        };
        var cells = members.ToDictionary(member => member, _ => new List<ComparisonCell>());
        foreach (var (label, value) in rows)
        {
            var texts = members.Select(member => value(member.Detail)).ToList();
            var differs = members.Count > 1 && texts.Distinct(StringComparer.Ordinal).Count() > 1;
            for (var index = 0; index < members.Count; index++)
            {
                cells[members[index]].Add(new ComparisonCell(label, texts[index], differs));
            }
        }

        foreach (var member in members)
        {
            member.Cells = cells[member];
            member.OnPropertyChanged(nameof(Cells));
        }
    }

    /// <summary>
    /// Shows a preview, letting go of the previous one.
    /// </summary>
    /// <param name="newLease">The lease on the bitmap, or <see langword="null"/> for none. The card owns it from now on.</param>
    public void SetPreview(ThumbnailLease? newLease)
    {
        var old = lease;
        lease = newLease;
        Preview = newLease?.Bitmap;
        old?.Dispose();
    }

    /// <inheritdoc />
    public void Dispose() => SetPreview(null);

    private static string MetadataText(DuplicateMemberDetail detail, Localizer localizer)
    {
        List<string> parts = [];
        if (detail.TagCount > 0)
        {
            parts.Add(localizer.Format("Duplicates_Meta_Tags", detail.TagCount));
        }

        if (detail.EditedFieldCount > 0)
        {
            parts.Add(localizer.Format("Duplicates_Meta_Edits", detail.EditedFieldCount));
        }

        if (detail.IsFavorite)
        {
            parts.Add(localizer.Get("Duplicates_Meta_Favorite"));
        }

        return parts.Count == 0 ? localizer.Get("Duplicates_Meta_None") : string.Join(", ", parts);
    }
}

/// <summary>
/// One attribute of one member, with whether the members differ in it.
/// </summary>
/// <param name="Label">The attribute's name.</param>
/// <param name="Text">The member's value.</param>
/// <param name="Differs">Whether the group's members don't all have this value, so the cell is tinted.</param>
public sealed record ComparisonCell(string Label, string Text, bool Differs);
