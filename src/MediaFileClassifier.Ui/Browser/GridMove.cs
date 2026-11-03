namespace MediaFileClassifier.Ui.Browser;

/// <summary>
/// A keyboard move of the focus in the media browser (E14-S03-T02).
/// </summary>
public enum GridMove
{
    /// <summary>The previous item.</summary>
    Left,

    /// <summary>The next item.</summary>
    Right,

    /// <summary>The item one row up.</summary>
    Up,

    /// <summary>The item one row down.</summary>
    Down,

    /// <summary>One screen up.</summary>
    PageUp,

    /// <summary>One screen down.</summary>
    PageDown,

    /// <summary>The first item.</summary>
    Home,

    /// <summary>The last item.</summary>
    End,
}
