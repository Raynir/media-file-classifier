using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A check box for a <see cref="FilterValueKind.Boolean"/> filter (E10-S02-T01), e.g. "Favorites only". Unticked means
/// the filter isn't set.
/// </summary>
public sealed partial class BooleanFilterViewModel : FilterEditorViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BooleanFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public BooleanFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the filter is on.
    /// </summary>
    [ObservableProperty]
    public partial bool IsOn { get; set; }

    /// <inheritdoc />
    public override FilterValue? Value => IsOn ? new BooleanValue(true) : null;

    /// <inheritdoc />
    public override string Describe(FilterValue value) => value is BooleanValue { Value: false } ? Localizer.Get("Filter_No") : Localizer.Get("Filter_Yes");

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value) => IsOn = value is BooleanValue { Value: true };

    partial void OnIsOnChanged(bool value) => NotifyChanged();
}
