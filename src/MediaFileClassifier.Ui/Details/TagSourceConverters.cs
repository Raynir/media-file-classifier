using Avalonia.Data.Converters;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Localization;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Value converters for the source icon on a tag chip (E07-S02).
/// </summary>
public static class TagSourceConverters
{
    /// <summary>
    /// Gets a converter from a tag's source to its small icon.
    /// </summary>
    public static IValueConverter Glyph { get; } = new FuncValueConverter<MetadataSource, string>(source => source switch
    {
        MetadataSource.User => "✎",
        MetadataSource.AI => "✦",
        _ => "▤",
    });

    /// <summary>
    /// Gets a converter from a tag's source to the tooltip that names it.
    /// </summary>
    public static IValueConverter Description { get; } = new FuncValueConverter<MetadataSource, string>(source => Localizer.Current.Get(source switch
    {
        MetadataSource.User => "Editor_Tag_Source_User",
        MetadataSource.AI => "Editor_Tag_Source_AI",
        _ => "Editor_Tag_Source_Technical",
    }));
}
