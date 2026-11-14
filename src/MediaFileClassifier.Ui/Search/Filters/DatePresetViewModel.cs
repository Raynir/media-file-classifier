namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// A choice in a date filter's preset list (E10-S02-T01).
/// </summary>
/// <param name="Id">The preset's ID, or <see langword="null"/> for "any time".</param>
/// <param name="Text">The text shown.</param>
public sealed record DatePresetViewModel(string? Id, string Text);
