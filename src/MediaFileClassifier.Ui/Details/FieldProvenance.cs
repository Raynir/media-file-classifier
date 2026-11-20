using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Metadata;

namespace MediaFileClassifier.Ui.Details;

/// <summary>
/// Whose value a field shows (E07-S11-T01): the AI's, the user's own edit, or an AI value the user confirmed (D020).
/// A value read from the file has no provenance mark.
/// </summary>
public enum FieldProvenance
{
    /// <summary>No mark: the field is empty or its value comes from the file.</summary>
    None,

    /// <summary>The effective value is the AI's.</summary>
    Ai,

    /// <summary>The user wrote the value.</summary>
    Edited,

    /// <summary>The user confirmed an AI value as it was.</summary>
    Accepted,
}

/// <summary>
/// Works out a field's provenance mark and the tooltip text that names the model and the date (E07-S11-T01), the same
/// for a field in the editor and, later, in bulk editing.
/// </summary>
/// <param name="localizer">UI strings.</param>
internal sealed class ProvenanceText(Localizer localizer)
{
    /// <summary>
    /// Gets or sets the model names of the item shown, by registration.
    /// </summary>
    public IReadOnlyDictionary<Id<ModelRegistration>, string> ModelNames { get; set; } = new Dictionary<Id<ModelRegistration>, string>();

    /// <summary>
    /// Gets which mark a field's effective value gets.
    /// </summary>
    /// <param name="view">The field's layers.</param>
    /// <returns>The mark.</returns>
    public static FieldProvenance Of(FieldMetadataView view) => view.Effective switch
    {
        { Source: MetadataSource.AI } => FieldProvenance.Ai,
        { Source: MetadataSource.User, Origin: UserValueOrigin.Accepted } => FieldProvenance.Accepted,
        { Source: MetadataSource.User } => FieldProvenance.Edited,
        _ => FieldProvenance.None,
    };

    /// <summary>
    /// Builds the tooltip of a field's mark.
    /// </summary>
    /// <param name="view">The field's layers.</param>
    /// <returns>The tooltip, or an empty text when the field has no mark.</returns>
    public string Tip(FieldMetadataView view)
    {
        if (view.Effective is not { } value)
        {
            return string.Empty;
        }

        var date = Format.DateTime(value.Timestamp);
        return Of(view) switch
        {
            FieldProvenance.Ai => Model(value.ModelId) is { } model
                ? localizer.Format("Provenance_Tip_Ai", model, date)
                : localizer.Format("Provenance_Tip_AiNoModel", date),
            FieldProvenance.Edited => localizer.Format("Provenance_Tip_Edited", date),
            FieldProvenance.Accepted => Model(value.ModelId) is { } model
                ? localizer.Format("Provenance_Tip_Accepted", model, date)
                : localizer.Format("Provenance_Tip_AcceptedNoModel", date),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Builds the tooltip of a field's new AI suggestion.
    /// </summary>
    /// <param name="suggestion">The suggested value.</param>
    /// <returns>The tooltip.</returns>
    public string SuggestionTip(AiFieldValue suggestion)
    {
        var date = Format.DateTime(suggestion.CreatedAt);
        return Model(suggestion.ModelId) is { } model
            ? localizer.Format("Provenance_Tip_Suggestion", model, date)
            : localizer.Format("Provenance_Tip_SuggestionNoModel", date);
    }

    private string? Model(Id<ModelRegistration>? id) => id is { } key && ModelNames.TryGetValue(key, out var name) ? name : null;
}
