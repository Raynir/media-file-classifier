namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// Validation of AI confidence scores.
/// </summary>
internal static class Confidences
{
    public static void EnsureValid(double? confidence)
    {
        if (confidence is { } value && (double.IsNaN(value) || value < 0 || value > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence), value, "Confidence must be between 0 and 1.");
        }
    }
}
