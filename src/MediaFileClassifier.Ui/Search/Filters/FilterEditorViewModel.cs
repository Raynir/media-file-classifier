using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// The control of one filter in the filter panel (E10-S02-T01). One subclass per <see cref="FilterValueKind"/>, so a
/// filter registered later shows up without UI changes.
/// </summary>
public abstract class FilterEditorViewModel : ViewModelBase
{
    private int loading;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilterEditorViewModel"/> class.
    /// </summary>
    /// <param name="definition">The filter.</param>
    /// <param name="localizer">UI strings.</param>
    protected FilterEditorViewModel(IFilterDefinition definition, Localizer localizer)
    {
        Definition = definition;
        Localizer = localizer;
        Title = localizer.Get(definition.DisplayNameKey);
    }

    /// <summary>
    /// Raised when the user changed the value.
    /// </summary>
    public event EventHandler? ValueChanged;

    /// <summary>
    /// Gets the filter.
    /// </summary>
    public IFilterDefinition Definition { get; }

    /// <summary>
    /// Gets the filter's ID.
    /// </summary>
    public string Id => Definition.Id;

    /// <summary>
    /// Gets the filter's display name.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the value the control holds, or <see langword="null"/> when it filters nothing.
    /// </summary>
    public abstract FilterValue? Value { get; }

    /// <summary>
    /// Gets UI strings.
    /// </summary>
    protected Localizer Localizer { get; }

    /// <summary>
    /// Sets the control from the active state, without raising <see cref="ValueChanged"/>.
    /// </summary>
    /// <param name="value">The filter's value, or <see langword="null"/> when it isn't set.</param>
    public void Load(FilterValue? value)
    {
        loading++;
        try
        {
            LoadCore(value);
        }
        finally
        {
            loading--;
        }
    }

    /// <summary>
    /// Describes a value for the filter's chip, without the filter's name.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The text, e.g. "Image, Video".</returns>
    public abstract string Describe(FilterValue value);

    /// <summary>
    /// Clears the control and reports it.
    /// </summary>
    public void Clear()
    {
        Load(null);
        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the control from a value.
    /// </summary>
    /// <param name="value">The value, or <see langword="null"/> for none.</param>
    protected abstract void LoadCore(FilterValue? value);

    /// <summary>
    /// Reports a change the user made, unless the control is being loaded.
    /// </summary>
    protected void NotifyChanged()
    {
        if (loading == 0)
        {
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the text of an option or preset: its resource key's text, else the fallback.
    /// </summary>
    /// <param name="key">The resource key, or <see langword="null"/>.</param>
    /// <param name="fallback">The text when there is no key or no text for it.</param>
    /// <returns>The text.</returns>
    protected string TextOf(string? key, string fallback) => (key is null ? null : Localizer.Find(key)) ?? fallback;
}
