namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// The values a <see cref="FilterValueKind.MultiSelect"/> or <see cref="FilterValueKind.IncludeExclude"/> filter offers,
/// for the filter panel's check boxes or picker, and the presets of a <see cref="FilterValueKind.DateRange"/> or
/// <see cref="FilterValueKind.Resolution"/> filter. A <see cref="FilterValueKind.Folder"/> filter offers the indexed folders for
/// no prefix and a folder's or subfolder's subfolders for its value as the prefix. A filter definition implements it when it has a list to offer.
/// </summary>
public interface IFilterOptions
{
    /// <summary>
    /// Gets the values to offer.
    /// </summary>
    /// <param name="prefix">Text typed into a picker, to narrow the list, or <see langword="null"/> for every value.</param>
    /// <param name="cancellationToken">Cancels the lookup.</param>
    /// <returns>The values, in display order.</returns>
    ValueTask<IReadOnlyList<FilterOption>> GetOptionsAsync(string? prefix, CancellationToken cancellationToken);
}

/// <summary>
/// One value a filter offers. The UI shows <see cref="DisplayNameKey"/>'s resource text if set, else
/// <see cref="DisplayName"/>, else <see cref="Value"/>.
/// </summary>
/// <param name="Value">The value stored in the filter state.</param>
/// <param name="DisplayNameKey">The resource key of its label, or <see langword="null"/>.</param>
/// <param name="DisplayName">Its label as is (e.g. a tag or <c>JPEG</c>), or <see langword="null"/>.</param>
/// <param name="IsOffline">Whether it can't be reached now, e.g. an Offline folder (D025), so the UI marks it. It can
/// still be picked: its items stay in the library.</param>
public sealed record FilterOption(string Value, string? DisplayNameKey = null, string? DisplayName = null, bool IsOffline = false);
