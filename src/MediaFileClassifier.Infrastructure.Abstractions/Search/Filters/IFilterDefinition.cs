namespace MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

/// <summary>
/// A filter (E10-S01, spec 1.11): what the filter panel shows and how the search applies it. Register an implementation
/// as a singleton <see cref="IFilterDefinition"/> and every search mode and the filter panel pick it up, with no change
/// to the search service. Derive from <see cref="FilterDefinition{TValue}"/>.
/// </summary>
public interface IFilterDefinition
{
    /// <summary>
    /// Raised when <see cref="IsAvailable"/> changes, on any thread.
    /// </summary>
    event EventHandler? AvailabilityChanged;

    /// <summary>
    /// Gets the filter's stable ID, in kebab case (e.g. <c>media-type</c>). Saved searches and the saved filter state
    /// store it, so it never changes.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the resource key of the filter's display name, which the UI looks up (e.g. <c>Filter_MediaType</c>).
    /// </summary>
    string DisplayNameKey { get; }

    /// <summary>
    /// Gets the filter's position in the filter panel: lower comes first.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Gets the kind of filter, which picks its control in the filter panel.
    /// </summary>
    FilterValueKind Kind { get; }

    /// <summary>
    /// Gets the type of its values, a <see cref="FilterValue"/>; a saved value is read back as this type.
    /// </summary>
    Type ValueType { get; }

    /// <summary>
    /// Gets a value indicating whether the filter can be used now. An unavailable filter is hidden and doesn't apply,
    /// e.g. when its feature is unavailable.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Builds the SQL condition an item must meet.
    /// </summary>
    /// <param name="value">The value, of <see cref="ValueType"/>.</param>
    /// <param name="sql">The SQL builder.</param>
    /// <returns>The condition, or <see langword="null"/> if the value filters nothing.</returns>
    /// <exception cref="ArgumentException">The value is of another type.</exception>
    string? ToSql(FilterValue value, IFilterSql sql);
}

/// <summary>
/// A filter whose value is a <typeparamref name="TValue"/>.
/// </summary>
/// <typeparam name="TValue">The value type.</typeparam>
public abstract class FilterDefinition<TValue> : IFilterDefinition
    where TValue : FilterValue
{
    /// <inheritdoc />
    public event EventHandler? AvailabilityChanged;

    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayNameKey { get; }

    /// <inheritdoc />
    public abstract int Order { get; }

    /// <inheritdoc />
    public abstract FilterValueKind Kind { get; }

    /// <inheritdoc />
    public Type ValueType => typeof(TValue);

    /// <inheritdoc />
    public virtual bool IsAvailable => true;

    /// <inheritdoc />
    public string? ToSql(FilterValue value, IFilterSql sql)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (value is not TValue typed)
        {
            throw new ArgumentException($"Filter {Id} takes a {typeof(TValue).Name}, not a {value?.GetType().Name ?? "null"}.", nameof(value));
        }

        return typed.IsEmpty ? null : ToSql(typed, sql);
    }

    /// <summary>
    /// Builds the SQL condition of a value that isn't empty.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <param name="sql">The SQL builder.</param>
    /// <returns>The condition, or <see langword="null"/> if the value filters nothing.</returns>
    protected abstract string? ToSql(TValue value, IFilterSql sql);

    /// <summary>
    /// Raises <see cref="AvailabilityChanged"/>.
    /// </summary>
    protected void RaiseAvailabilityChanged() => AvailabilityChanged?.Invoke(this, EventArgs.Empty);
}
