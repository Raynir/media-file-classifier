using System.Collections.ObjectModel;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A check box list for a <see cref="FilterValueKind.MultiSelect"/> filter (E10-S02-T01): a value matches any ticked
/// option. The options come from <see cref="IFilterOptions"/>.
/// </summary>
public sealed class MultiSelectFilterViewModel : FilterEditorViewModel
{
    private readonly Dictionary<string, string> names = new(StringComparer.Ordinal);
    private IReadOnlyList<string> pending = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="MultiSelectFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public MultiSelectFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
    }

    /// <summary>
    /// Gets the check boxes.
    /// </summary>
    public ObservableCollection<FilterOptionViewModel> Options { get; } = [];

    /// <inheritdoc />
    public override FilterValue? Value
    {
        get
        {
            var ticked = Options.Where(option => option.IsChecked).Select(option => option.Value).ToList();
            return ticked.Count == 0 ? null : new MultiSelectValue(ticked);
        }
    }

    /// <summary>
    /// Loads the options.
    /// </summary>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>A task that completes when the options are there.</returns>
    public async Task LoadOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (Definition is not IFilterOptions provider)
        {
            return;
        }

        var options = await provider.GetOptionsAsync(null, cancellationToken).ConfigureAwait(true);
        Options.Clear();
        foreach (var option in options)
        {
            var text = TextOf(option.DisplayNameKey, option.DisplayName ?? option.Value);
            names[option.Value] = text;
            var item = new FilterOptionViewModel(option.Value, text);
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(FilterOptionViewModel.IsChecked))
                {
                    NotifyChanged();
                }
            };
            Options.Add(item);
        }

        Load(new MultiSelectValue(pending));
    }

    /// <inheritdoc />
    public override string Describe(FilterValue value) =>
        value is MultiSelectValue multi ? string.Join(", ", multi.Values.Select(item => names.GetValueOrDefault(item, item))) : string.Empty;

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value)
    {
        // The value may come before the options do (restored state): keep it for when they load.
        pending = value is MultiSelectValue multi ? multi.Values : [];
        foreach (var option in Options)
        {
            option.IsChecked = pending.Contains(option.Value, StringComparer.Ordinal);
        }
    }
}
