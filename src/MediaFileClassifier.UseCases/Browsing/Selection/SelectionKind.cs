namespace MediaFileClassifier.UseCases.Browsing.Selection;

/// <summary>
/// How many items a selection holds, so the details panel can switch to bulk mode (E07-S03).
/// </summary>
public enum SelectionKind
{
    /// <summary>Nothing is selected.</summary>
    None,

    /// <summary>One item is selected.</summary>
    Single,

    /// <summary>Several items are selected.</summary>
    Multiple,
}
