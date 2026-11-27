namespace MediaFileClassifier.Domain.Duplicates;

/// <summary>
/// How alike two items must look to be grouped as visual near-duplicates (E12-S02-T03). A library setting.
/// </summary>
public enum VisualDuplicateSensitivity
{
    /// <summary>
    /// Copies saved differently: re-encoded, resized, converted, thumbnails, and light tonal edits. The default.
    /// </summary>
    Strict = 0,

    /// <summary>
    /// Also crops, edits and burst shots.
    /// </summary>
    Loose = 1,
}
