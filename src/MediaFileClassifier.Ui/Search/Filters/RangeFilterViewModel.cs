using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// Two number boxes for a <see cref="FilterValueKind.Range"/> filter (E10-S02-T01): from and to, either one may be left
/// empty. Text that isn't a number counts as empty.
/// </summary>
public sealed partial class RangeFilterViewModel : FilterEditorViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RangeFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public RangeFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
        if (definition is IFilterRangeInfo info)
        {
            Unit = info.UnitKey is null ? null : localizer.Find(info.UnitKey);
            Minimum = info.Minimum;
            Maximum = info.Maximum;
        }
    }

    /// <summary>
    /// Gets the unit shown beside the boxes, or <see langword="null"/>.
    /// </summary>
    public string? Unit { get; }

    /// <summary>
    /// Gets the lowest value allowed, or <see langword="null"/>.
    /// </summary>
    public double? Minimum { get; }

    /// <summary>
    /// Gets the highest value allowed, or <see langword="null"/>.
    /// </summary>
    public double? Maximum { get; }

    /// <summary>
    /// Gets or sets the "from" box.
    /// </summary>
    [ObservableProperty]
    public partial string MinText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the "to" box.
    /// </summary>
    [ObservableProperty]
    public partial string MaxText { get; set; } = string.Empty;

    /// <inheritdoc />
    public override FilterValue? Value
    {
        get
        {
            var (min, max) = (Parse(MinText), Parse(MaxText));
            var value = min > max ? new RangeValue(max, min) : new RangeValue(min, max);
            return value.IsEmpty ? null : value;
        }
    }

    /// <inheritdoc />
    public override string Describe(FilterValue value)
    {
        if (value is not RangeValue range)
        {
            return string.Empty;
        }

        var unit = Unit is null ? string.Empty : " " + Unit;
        return (range.Min, range.Max) switch
        {
            ({ } min, { } max) => string.Create(CultureInfo.CurrentCulture, $"{min:0.##}–{max:0.##}{unit}"),
            ({ } min, null) => string.Create(CultureInfo.CurrentCulture, $"≥ {min:0.##}{unit}"),
            (null, { } max) => string.Create(CultureInfo.CurrentCulture, $"≤ {max:0.##}{unit}"),
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    public override void Normalize() => Load(Value);

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value)
    {
        var range = value as RangeValue;
        MinText = range?.Min is { } min ? min.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
        MaxText = range?.Max is { } max ? max.ToString("0.##", CultureInfo.CurrentCulture) : string.Empty;
    }

    partial void OnMinTextChanged(string value) => NotifyChanged();

    partial void OnMaxTextChanged(string value) => NotifyChanged();

    private double? Parse(string text)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) || !double.IsFinite(number))
        {
            return null;
        }

        return Math.Clamp(number, Minimum ?? double.MinValue, Maximum ?? double.MaxValue);
    }
}
