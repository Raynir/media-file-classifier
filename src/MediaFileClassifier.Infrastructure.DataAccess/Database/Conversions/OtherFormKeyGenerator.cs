using MediaFileClassifier.Domain.Metadata;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;

/// <summary>
/// Fills the <c>other_form_key</c> column of a tag assignment when it's added: its other-language form in upper case, like
/// <c>tag_key</c> (D071), or nothing when the tag has no other form. The tag filter and autocompletion match it with an
/// index (D348).
/// </summary>
internal sealed class OtherFormKeyGenerator : ValueGenerator<string?>
{
    /// <summary>
    /// Name of the shadow property that holds the key.
    /// </summary>
    public const string Property = "OtherFormKey";

    /// <inheritdoc />
    public override bool GeneratesTemporaryValues => false;

    /// <summary>
    /// Gets the key of an other-language form.
    /// </summary>
    /// <param name="form">The form.</param>
    /// <returns>The form in upper case, or <see langword="null"/> without a form.</returns>
    public static string? KeyOf(string? form) => string.IsNullOrWhiteSpace(form) ? null : TagKeyGenerator.KeyOf(TagName.From(form));

    /// <inheritdoc />
    public override string? Next(EntityEntry entry) => entry.Entity switch
    {
        TagAssignment assignment => KeyOf(assignment.OtherForm),
        _ => throw new InvalidOperationException($"{entry.Metadata.ClrType.Name} has no other form."),
    };
}
