using MediaFileClassifier.Domain.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Fills the <c>tag_key</c> column of a tag row when it's added: the tag in upper case, so equal keys mean equal
/// <see cref="TagName"/>s. <see cref="TagName"/> ignores case with <see cref="StringComparison.OrdinalIgnoreCase"/>,
/// which compares like <see cref="string.ToUpperInvariant"/> in every alphabet; SQLite's <c>NOCASE</c> folds only
/// A–Z, so "Отпуск" and "отпуск" would be two tags.
/// </summary>
internal sealed class TagKeyGenerator : ValueGenerator<string>
{
    /// <summary>
    /// Name of the shadow property that holds the key.
    /// </summary>
    public const string Property = "TagKey";

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Gets the key of a tag.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The tag in upper case.</returns>
    public static string KeyOf(TagName tag) => tag.Value.ToUpperInvariant();

    /// <inheritdoc />
    public override string Next(EntityEntry entry) => entry.Entity switch
    {
        TagAssignment assignment => KeyOf(assignment.Tag),
        TagRejection rejection => KeyOf(rejection.Tag),
        _ => throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} has no tag."),
    };
}
