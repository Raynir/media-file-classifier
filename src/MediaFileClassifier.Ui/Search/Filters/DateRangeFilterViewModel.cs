using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A preset list and a custom range for a <see cref="FilterValueKind.DateRange"/> filter (E10-S02-T01). The presets come
/// from <see cref="IFilterPresets"/>. A preset and a custom range exclude each other: picking one clears the other. The
/// last day of the custom range is inclusive here; the value's end is the next midnight.
/// </summary>
public sealed partial class DateRangeFilterViewModel : FilterEditorViewModel
{
    private readonly DatePresetViewModel anyTime;
    private bool syncing;

    /// <summary>
    /// Initializes a new instance of the <see cref="DateRangeFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public DateRangeFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
        anyTime = new DatePresetViewModel(null, localizer.Get("Filter_AnyTime"));
        Presets =
        [
            anyTime,
            .. FilterPresetList.Of(definition).Select(preset => new DatePresetViewModel(preset.Id, TextOf(preset.DisplayNameKey, preset.Id))),
        ];
        SelectedPreset = anyTime;
    }

    /// <summary>
    /// Gets the presets, "any time" first.
    /// </summary>
    public IReadOnlyList<DatePresetViewModel> Presets { get; }

    /// <summary>
    /// Gets or sets the chosen preset.
    /// </summary>
    [ObservableProperty]
    public partial DatePresetViewModel SelectedPreset { get; set; }

    /// <summary>
    /// Gets or sets the first day of the custom range, or <see langword="null"/> for no start.
    /// </summary>
    [ObservableProperty]
    public partial DateTime? FromDate { get; set; }

    /// <summary>
    /// Gets or sets the last day of the custom range, or <see langword="null"/> for no end.
    /// </summary>
    [ObservableProperty]
    public partial DateTime? ToDate { get; set; }

    /// <inheritdoc />
    public override FilterValue? Value
    {
        get
        {
            var (first, last) = FromDate > ToDate ? (ToDate, FromDate) : (FromDate, ToDate);
            DateRangeValue value = SelectedPreset.Id is { } preset
                ? new(preset, null, null)
                : new(null, first is { } from ? new DateTimeOffset(from.Date) : null, last is { } to ? new DateTimeOffset(to.Date.AddDays(1)) : null);
            return value.IsEmpty ? null : value;
        }
    }

    /// <inheritdoc />
    public override string Describe(FilterValue value)
    {
        if (value is not DateRangeValue range)
        {
            return string.Empty;
        }

        if (range.Preset is { } preset)
        {
            return Presets.FirstOrDefault(item => item.Id == preset)?.Text ?? preset;
        }

        var from = range.From?.LocalDateTime.ToString("d", CultureInfo.CurrentCulture);
        var to = range.To?.LocalDateTime.AddDays(-1).ToString("d", CultureInfo.CurrentCulture);
        return (from, to) switch
        {
            ({ } start, { } end) => $"{start} – {end}",
            ({ } start, null) => Localizer.Format("Filter_DateFrom", start),
            (null, { } end) => Localizer.Format("Filter_DateUntil", end),
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value)
    {
        var range = value as DateRangeValue;
        Silently(() =>
        {
            SelectedPreset = Presets.FirstOrDefault(item => item.Id is not null && item.Id == range?.Preset) ?? anyTime;
            FromDate = range?.Preset is null ? range?.From?.LocalDateTime.Date : null;
            ToDate = range?.Preset is null && range?.To is { } to ? to.LocalDateTime.AddDays(-1).Date : null;
        });
    }

    partial void OnSelectedPresetChanged(DatePresetViewModel value)
    {
        if (syncing)
        {
            return;
        }

        if (value.Id is not null)
        {
            Silently(() =>
            {
                FromDate = null;
                ToDate = null;
            });
        }

        NotifyChanged();
    }

    partial void OnFromDateChanged(DateTime? value) => OnCustomDateChanged(value);

    partial void OnToDateChanged(DateTime? value) => OnCustomDateChanged(value);

    private void Silently(Action action)
    {
        syncing = true;
        try
        {
            action();
        }
        finally
        {
            syncing = false;
        }
    }

    private void OnCustomDateChanged(DateTime? value)
    {
        if (syncing)
        {
            return;
        }

        if (value is not null && SelectedPreset.Id is not null)
        {
            Silently(() => SelectedPreset = anyTime);
        }

        NotifyChanged();
    }
}
