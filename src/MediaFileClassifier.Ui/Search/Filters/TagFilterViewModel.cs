using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A tag picker for a <see cref="FilterValueKind.IncludeExclude"/> filter (E10-S02-T01): tags to include (matching any
/// or all of them) and tags to exclude, with suggestions from <see cref="IFilterOptions"/> as the user types.
/// </summary>
public sealed partial class TagFilterViewModel : FilterEditorViewModel
{
    private int lookup;

    /// <summary>
    /// Initializes a new instance of the <see cref="TagFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public TagFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
    }

    /// <summary>
    /// Gets the tags to include.
    /// </summary>
    public ObservableCollection<string> Include { get; } = [];

    /// <summary>
    /// Gets the tags to exclude.
    /// </summary>
    public ObservableCollection<string> Exclude { get; } = [];

    /// <summary>
    /// Gets the tags that start with what is typed.
    /// </summary>
    public ObservableCollection<string> Suggestions { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether an item must have every included tag (AND) rather than any (OR).
    /// </summary>
    [ObservableProperty]
    public partial bool MatchAll { get; set; }

    /// <summary>
    /// Gets or sets the text typed in the tag box.
    /// </summary>
    [ObservableProperty]
    public partial string Query { get; set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether there are suggestions to show.
    /// </summary>
    public bool HasSuggestions => Suggestions.Count > 0;

    /// <inheritdoc />
    public override FilterValue? Value
    {
        get
        {
            var value = new IncludeExcludeValue(Include, Exclude, MatchAll);
            return value.IsEmpty ? null : value;
        }
    }

    /// <summary>
    /// Looks up the suggestions for the text typed.
    /// </summary>
    /// <returns>A task that completes when the suggestions are updated.</returns>
    public async Task UpdateSuggestionsAsync()
    {
        var current = ++lookup;
        var prefix = Query.Trim();
        if (prefix.Length == 0 || Definition is not IFilterOptions provider)
        {
            Suggestions.Clear();
            OnPropertyChanged(nameof(HasSuggestions));
            return;
        }

        try
        {
            var options = await provider.GetOptionsAsync(prefix, CancellationToken.None).ConfigureAwait(true);
            if (current != lookup)
            {
                return;
            }

            Suggestions.Clear();
            foreach (var option in options.Select(option => TextOf(option.DisplayNameKey, option.DisplayName ?? option.Value)).Except([.. Include, .. Exclude]).Take(8))
            {
                Suggestions.Add(option);
            }

            OnPropertyChanged(nameof(HasSuggestions));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // No suggestions; typing a tag and pressing Enter still works.
            Suggestions.Clear();
        }
    }

    /// <inheritdoc />
    public override string Describe(FilterValue value)
    {
        if (value is not IncludeExcludeValue tags)
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (tags.Include.Count > 0)
        {
            parts.Add(string.Join(tags.MatchAll ? " + " : ", ", tags.Include));
        }

        if (tags.Exclude.Count > 0)
        {
            parts.Add(Localizer.Format("Filter_TagsExcluding", string.Join(", ", tags.Exclude)));
        }

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Includes a tag.
    /// </summary>
    /// <param name="tag">The tag, or <see langword="null"/> for the text typed.</param>
    [RelayCommand]
    public void AddInclude(string? tag) => Add(tag, Include, Exclude);

    /// <summary>
    /// Excludes a tag.
    /// </summary>
    /// <param name="tag">The tag, or <see langword="null"/> for the text typed.</param>
    [RelayCommand]
    public void AddExclude(string? tag) => Add(tag, Exclude, Include);

    /// <summary>
    /// Stops including a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    [RelayCommand]
    public void RemoveInclude(string? tag) => Remove(tag, Include);

    /// <summary>
    /// Stops excluding a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    [RelayCommand]
    public void RemoveExclude(string? tag) => Remove(tag, Exclude);

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value)
    {
        var tags = value as IncludeExcludeValue;
        Include.Clear();
        Exclude.Clear();
        foreach (var tag in tags?.Include ?? [])
        {
            Include.Add(tag);
        }

        foreach (var tag in tags?.Exclude ?? [])
        {
            Exclude.Add(tag);
        }

        MatchAll = tags?.MatchAll ?? false;
    }

    partial void OnMatchAllChanged(bool value)
    {
        if (Include.Count > 0)
        {
            NotifyChanged();
        }
    }

    partial void OnQueryChanged(string value) => _ = UpdateSuggestionsAsync();

    private void Add(string? tag, ObservableCollection<string> target, ObservableCollection<string> other)
    {
        var text = (tag ?? Query).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (other.FirstOrDefault(item => string.Equals(item, text, StringComparison.OrdinalIgnoreCase)) is { } opposite)
        {
            other.Remove(opposite);
        }

        if (!target.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            target.Add(text);
        }

        Query = string.Empty;
        NotifyChanged();
    }

    private void Remove(string? tag, ObservableCollection<string> target)
    {
        if (tag is not null && target.Remove(tag))
        {
            NotifyChanged();
        }
    }
}
