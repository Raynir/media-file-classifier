using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A preset list and two boxes for a <see cref="FilterValueKind.Resolution"/> filter (E10-S02-T01): at least this wide
/// and this tall. A preset wins over the boxes, so picking one clears them and typing in one resets the preset.
/// </summary>
public sealed partial class ResolutionFilterViewModel : FilterEditorViewModel
{
    private readonly DatePresetViewModel anyResolution;
    private bool syncing;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolutionFilterViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    public ResolutionFilterViewModel(IFilterDefinition definition, Localizer localizer)
        : base(definition, localizer)
    {
        anyResolution = new DatePresetViewModel(null, localizer.Get("Filter_AnyResolution"));
        Presets =
        [
            anyResolution,
            .. FilterPresetList.Of(definition).Select(preset => new DatePresetViewModel(preset.Id, TextOf(preset.DisplayNameKey, preset.Id))),
        ];
        SelectedPreset = anyResolution;
    }

    /// <summary>
    /// Gets the presets, "any" first.
    /// </summary>
    public IReadOnlyList<DatePresetViewModel> Presets { get; }

    /// <summary>
    /// Gets or sets the chosen preset.
    /// </summary>
    [ObservableProperty]
    public partial DatePresetViewModel SelectedPreset { get; set; }

    /// <summary>
    /// Gets or sets the minimum width box, in pixels.
    /// </summary>
    [ObservableProperty]
    public partial string MinWidthText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the minimum height box, in pixels.
    /// </summary>
    [ObservableProperty]
    public partial string MinHeightText { get; set; } = string.Empty;

    /// <inheritdoc />
    public override FilterValue? Value
    {
        get
        {
            ResolutionValue value = SelectedPreset.Id is { } preset ? new(preset, null, null) : new(null, Parse(MinWidthText), Parse(MinHeightText));
            return value.IsEmpty ? null : value;
        }
    }

    /// <inheritdoc />
    public override string Describe(FilterValue value)
    {
        if (value is not ResolutionValue resolution)
        {
            return string.Empty;
        }

        if (resolution.Preset is { } preset)
        {
            return Presets.FirstOrDefault(item => item.Id == preset)?.Text ?? preset;
        }

        return (resolution.MinWidth, resolution.MinHeight) switch
        {
            ({ } width, { } height) => string.Create(CultureInfo.CurrentCulture, $"≥ {width} × {height} px"),
            ({ } width, null) => string.Create(CultureInfo.CurrentCulture, $"≥ {width} px wide"),
            (null, { } height) => string.Create(CultureInfo.CurrentCulture, $"≥ {height} px tall"),
            _ => string.Empty,
        };
    }

    /// <inheritdoc />
    protected override void LoadCore(FilterValue? value)
    {
        var resolution = value as ResolutionValue;
        Silently(() =>
        {
            SelectedPreset = Presets.FirstOrDefault(item => item.Id is not null && item.Id == resolution?.Preset) ?? anyResolution;
            MinWidthText = resolution?.Preset is null && resolution?.MinWidth is { } width ? width.ToString(CultureInfo.CurrentCulture) : string.Empty;
            MinHeightText = resolution?.Preset is null && resolution?.MinHeight is { } height ? height.ToString(CultureInfo.CurrentCulture) : string.Empty;
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
                MinWidthText = string.Empty;
                MinHeightText = string.Empty;
            });
        }

        NotifyChanged();
    }

    partial void OnMinWidthTextChanged(string value) => OnBoxChanged(value);

    partial void OnMinHeightTextChanged(string value) => OnBoxChanged(value);

    private static int? Parse(string text) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var number) && number > 0 ? number : null;

    private void OnBoxChanged(string value)
    {
        if (syncing)
        {
            return;
        }

        if (value.Length > 0 && SelectedPreset.Id is not null)
        {
            Silently(() => SelectedPreset = anyResolution);
        }

        NotifyChanged();
    }

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
}
