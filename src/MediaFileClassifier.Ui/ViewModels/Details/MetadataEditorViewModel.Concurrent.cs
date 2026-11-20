using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.UseCases.Metadata;
using MediaFileClassifier.UseCases.Metadata.GetItemMetadata;

namespace MediaFileClassifier.Ui.ViewModels.Details;

/// <summary>
/// The editor's handling of changes made underneath it (E07-S12-T02), e.g. a re-analysis that finishes while the user
/// edits.
/// </summary>
public sealed partial class MetadataEditorViewModel
{
    /// <summary>
    /// Brings the editor up to date after the shown item changed in the background, without discarding staged changes.
    /// Fields and tags with nothing staged show the stored values. A staged field that changed as well keeps the user's
    /// text and is named in a notice; so is a staged tag change that the stored tags no longer fit.
    /// </summary>
    /// <param name="changedFields">The fields that changed.</param>
    /// <param name="tagsChanged">Whether the tags changed.</param>
    /// <returns>A task that completes when the editor shows the merged state.</returns>
    public async Task MergeChangesAsync(IReadOnlySet<DescriptiveField> changedFields, bool tagsChanged)
    {
        ArgumentNullException.ThrowIfNull(changedFields);

        if (ItemId is not { } id || IsSaving)
        {
            // A save reloads the editor itself, and its own change notice arrives while it runs.
            return;
        }

        var load = loads;
        ItemMetadataView? view;
        try
        {
            view = await sender.Send(new GetItemMetadataQuery(id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogLoadFailed(logger, ex);
            return;
        }

        if (load != loads || ItemId != id || IsSaving)
        {
            // Another item or load took over, or a save started while the metadata was read.
            return;
        }

        // The staged state is read only now, after the await, so edits made while reading are kept too.
        if (!IsDirty)
        {
            Apply(view);
            return;
        }

        MergeInto(view, changedFields, tagsChanged);
    }

    private void MergeInto(ItemMetadataView? view, IReadOnlySet<DescriptiveField> changedFields, bool tagsChanged)
    {
        provenances.ModelNames = view?.ModelNames ?? provenances.ModelNames;
        var overlapping = new List<string>();
        foreach (var field in AllFields.Append(Transcript))
        {
            var stored = view is null ? new FieldMetadataView(field.Field, null, null, null, null, null) : view[field.Field];
            if (field.IsDirty)
            {
                if (changedFields.Contains(field.Field))
                {
                    overlapping.Add(field.Label);
                }

                field.Rebase(stored);
            }
            else
            {
                field.Load(stored);
            }
        }

        HasTranscript = Transcript.HasText;
        shown = view;
        if (MergeTags(view, tagsChanged))
        {
            overlapping.Add(localizer.Get("Editor_Notice_TagsName"));
        }

        UpdateDirty();
        if (overlapping.Count > 0 && IsDirty)
        {
            Notice = localizer.Format("Editor_Notice_Changed", string.Join(", ", overlapping));
        }
    }

    // Shows the stored tags with the staged tag changes on top. Returns true when a staged change no longer fits the
    // stored tags: a tag to remove or reject is gone, or a tag the user added is stored now.
    private bool MergeTags(ItemMetadataView? view, bool tagsChanged)
    {
        var staged = Tags.Where(chip => chip.IsStaged).ToList();
        ApplyTags(view);
        var conflict = false;
        foreach (var chip in staged)
        {
            var stored = Tags.FirstOrDefault(tag => string.Equals(tag.Text, chip.Text, StringComparison.OrdinalIgnoreCase));
            if (chip.Stage == TagStage.Added)
            {
                if (stored is null)
                {
                    AddChip(new TagChipViewModel(chip.Text, MetadataSource.User, stage: TagStage.Added));
                }
                else
                {
                    conflict |= tagsChanged;
                }
            }
            else if (stored is null)
            {
                conflict |= tagsChanged;
            }
            else
            {
                stored.Stage = chip.Stage;
                if (stored.Suggestion is not null && stored.Suggestion == chip.Suggestion)
                {
                    stored.SuggestionStaged = chip.SuggestionStaged;
                }
                else if (chip.SuggestionStaged != SuggestionStage.None)
                {
                    // The suggestion is gone or has changed meaning, so what was staged on it isn't carried over.
                    conflict |= tagsChanged;
                }
            }
        }

        HasTags = Tags.Count > 0;
        return conflict;
    }
}
