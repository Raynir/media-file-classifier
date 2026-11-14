using System.Diagnostics.CodeAnalysis;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.Abstractions.Search;

namespace MediaFileClassifier.UseCases.Search.SearchResults;

/// <summary>
/// An order the user picked for the results (E10-S04-T02): a sort mode and whether it runs descending. Relevance is
/// always best first, so its direction is ignored.
/// </summary>
/// <param name="Mode">The sort mode.</param>
/// <param name="Descending">Whether it runs descending: Z to A, newest, largest first.</param>
public sealed record SortChoice(SortMode Mode, bool Descending)
{
    /// <summary>
    /// Gets the default while searching: relevance (E10-S04).
    /// </summary>
    public static SortChoice SearchDefault { get; } = new(SortMode.Relevance, Descending: true);

    /// <summary>
    /// Gets the default while browsing: date taken, newest first (E10-S04).
    /// </summary>
    public static SortChoice BrowseDefault { get; } = new(SortMode.Date, Descending: true);

    /// <summary>
    /// Gets the value of <see cref="SearchQuery.ReverseSort"/> for this order: the search reverses a mode's natural
    /// direction, which is A to Z for names and descending for the others.
    /// </summary>
    public bool ReverseSort => Mode switch
    {
        SortMode.Relevance => false,
        SortMode.Name => Descending,
        _ => !Descending,
    };

    /// <summary>
    /// Makes the order a mode starts with when the user picks it: names A to Z, the others newest or largest first.
    /// </summary>
    /// <param name="mode">The mode.</param>
    /// <returns>The order.</returns>
    public static SortChoice Natural(SortMode mode) => new(mode, Descending: mode != SortMode.Name);

    /// <summary>
    /// Reads a stored order, as <see cref="ToString"/> writes it.
    /// </summary>
    /// <param name="text">The stored text, e.g. <c>Name asc</c>.</param>
    /// <param name="choice">The order, if the text is one.</param>
    /// <returns><see langword="true"/> if the text is an order.</returns>
    public static bool TryParse(string? text, [NotNullWhen(true)] out SortChoice? choice)
    {
        choice = null;
        var parts = text?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is not { Length: 2 }
            || !Enum.TryParse<SortMode>(parts[0], ignoreCase: false, out var mode)
            || !Enum.IsDefined(mode)
            || parts[1] is not ("asc" or "desc"))
        {
            return false;
        }

        choice = new SortChoice(mode, parts[1] == "desc");
        return true;
    }

    /// <summary>
    /// Writes the order for the settings, e.g. <c>Name asc</c>.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"{Mode} {(Descending ? "desc" : "asc")}";
}
