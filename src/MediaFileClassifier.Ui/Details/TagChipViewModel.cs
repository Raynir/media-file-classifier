using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// One tag chip in the metadata editor (E07-S02): the tag and where it comes from.
/// </summary>
public sealed partial class TagChipViewModel : ViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TagChipViewModel"/> class.
    /// </summary>
    /// <param name="text">The tag.</param>
    /// <param name="source">Where the tag comes from.</param>
    /// <param name="confidence">The AI confidence, for an AI tag.</param>
    public TagChipViewModel(string text, MetadataSource source, double? confidence = null)
    {
        Text = text;
        Source = source;
        Confidence = confidence;
    }

    /// <summary>
    /// Gets the tag.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets where the tag comes from.
    /// </summary>
    public MetadataSource Source { get; }

    /// <summary>
    /// Gets the AI confidence of an AI tag, or <see langword="null"/>.
    /// </summary>
    public double? Confidence { get; }

    /// <summary>
    /// Gets a value indicating whether the user has this tag (typed, or accepted from the AI).
    /// </summary>
    public bool IsUser => Source == MetadataSource.User;
}
