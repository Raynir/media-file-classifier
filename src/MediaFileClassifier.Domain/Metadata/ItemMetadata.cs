using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;

namespace MediaFileClassifier.Domain.Metadata;

/// <summary>
/// All descriptive metadata of one item across the three layers, and the rules that combine them.
/// </summary>
/// <remarks>
/// <para><b>Effective value rule:</b> User, then AI, then Technical (spec 1.5). The AI layer is made of the values of
/// each analyzer's active run, unless that run was rejected (D021). When two analyzers produce the same field,
/// the newer run wins.</para>
/// <para><b>Re-analysis (D020):</b> a new run becomes active for its analyzer and never touches user values,
/// whether edited or accepted. When its AI value differs from a user value, the user value is flagged with a
/// new AI suggestion, which only the user can accept or dismiss. Fields without a user value take the new AI value.</para>
/// <para><b>Tags:</b> each assignment keeps its source. Rejecting a tag leaves a tombstone, so AI and technical
/// assignments of that tag are ignored from then on, including those of future runs.</para>
/// <para><b>Retention:</b> besides the active run, a number of previous runs per analyzer are kept for comparison.
/// Older runs are pruned with their values and tags. Accepted values keep a <see cref="RunReference"/> snapshot,
/// so they stay meaningful after their run is pruned.</para>
/// </remarks>
public sealed class ItemMetadata
{
    private readonly Dictionary<DescriptiveField, TechnicalFieldValue> technicalValues = [];
    private readonly Dictionary<DescriptiveField, UserFieldValue> userValues = [];
    private readonly List<AnalysisRun> runs = [];
    private readonly List<AiFieldValue> aiValues = [];
    private readonly List<TagAssignment> tags = [];
    private readonly Dictionary<TagName, TagRejection> rejections = [];
    private readonly Dictionary<TagName, TagSuggestion> tagSuggestions = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ItemMetadata"/> class from stored values.
    /// </summary>
    /// <param name="itemId">The item.</param>
    /// <param name="technicalValues">Descriptive values read from the file.</param>
    /// <param name="userValues">User-owned values.</param>
    /// <param name="runs">Retained completed analysis runs.</param>
    /// <param name="aiValues">Values of the retained runs.</param>
    /// <param name="tags">Tag assignments of every source, including those of the retained runs.</param>
    /// <param name="rejections">Tag tombstones.</param>
    /// <param name="tagSuggestions">New AI suggestions on tags.</param>
    public ItemMetadata(
        Id<MediaItem> itemId,
        IEnumerable<TechnicalFieldValue>? technicalValues = null,
        IEnumerable<UserFieldValue>? userValues = null,
        IEnumerable<AnalysisRun>? runs = null,
        IEnumerable<AiFieldValue>? aiValues = null,
        IEnumerable<TagAssignment>? tags = null,
        IEnumerable<TagRejection>? rejections = null,
        IEnumerable<TagSuggestion>? tagSuggestions = null)
    {
        ItemId = itemId;

        foreach (var run in runs ?? [])
        {
            EnsureItem(run.ItemId);
            if (run.IsActive && this.runs.Any(other => other.IsActive && other.Analyzer == run.Analyzer))
            {
                throw new ArgumentException($"Analyzer {run.Analyzer} has more than one active run.", nameof(runs));
            }

            this.runs.Add(run);
        }

        foreach (var value in technicalValues ?? [])
        {
            EnsureItem(value.ItemId);
            this.technicalValues[value.Field] = value;
        }

        foreach (var value in userValues ?? [])
        {
            EnsureItem(value.ItemId);
            this.userValues[value.Field] = value;
        }

        foreach (var value in aiValues ?? [])
        {
            EnsureItem(value.ItemId);
            EnsureRetained(value.RunId);
            this.aiValues.Add(value);
        }

        foreach (var tag in tags ?? [])
        {
            EnsureItem(tag.ItemId);
            if (tag.RunId is { } runId)
            {
                EnsureRetained(runId);
            }

            this.tags.Add(tag);
        }

        foreach (var rejection in rejections ?? [])
        {
            EnsureItem(rejection.ItemId);
            this.rejections[rejection.Tag] = rejection;
        }

        foreach (var suggestion in tagSuggestions ?? [])
        {
            EnsureItem(suggestion.ItemId);
            EnsureRetained(suggestion.RunId);
            this.tagSuggestions[suggestion.Tag] = suggestion;
        }
    }

    public Id<MediaItem> ItemId { get; }

    public IReadOnlyCollection<TechnicalFieldValue> TechnicalValues => technicalValues.Values;

    public IReadOnlyCollection<UserFieldValue> UserValues => userValues.Values;

    public IReadOnlyList<AnalysisRun> Runs => runs;

    public IReadOnlyList<AiFieldValue> AiValues => aiValues;

    public IReadOnlyList<TagAssignment> Tags => tags;

    public IReadOnlyCollection<TagRejection> Rejections => rejections.Values;

    /// <summary>
    /// Gets the tags with a new AI suggestion: an added tag the user doesn't have, or an accepted tag the AI dropped.
    /// </summary>
    public IReadOnlyCollection<TagSuggestion> TagSuggestions => tagSuggestions.Values;

    /// <summary>
    /// Gets a value indicating whether a field or tag has a new AI suggestion (D020).
    /// </summary>
    public bool HasNewAiSuggestions => tagSuggestions.Count > 0 || userValues.Values.Any(value => value.HasNewAiSuggestion);

    public AnalysisRun? GetActiveRun(string analyzer) =>
        runs.SingleOrDefault(run => run.IsActive && run.Analyzer == analyzer);

    /// <summary>
    /// Gets the retained previous runs of an analyzer, newest first, for comparing with the active run.
    /// </summary>
    /// <param name="analyzer">Analyzer key.</param>
    /// <returns>The previous runs.</returns>
    public IReadOnlyList<AnalysisRun> GetPreviousRuns(string analyzer) =>
        [.. NewestFirst(runs.Where(run => !run.IsActive && run.Analyzer == analyzer))];

    public IReadOnlyList<AiFieldValue> GetRunValues(Id<AnalysisRun> runId) =>
        [.. aiValues.Where(value => value.RunId == runId)];

    public IReadOnlyList<TagAssignment> GetRunTags(Id<AnalysisRun> runId) =>
        [.. tags.Where(tag => tag.RunId == runId)];

    /// <summary>
    /// Gets the user layer's value of a field, whether edited or accepted.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The user value, or <c>null</c>.</returns>
    public UserFieldValue? GetUserValue(DescriptiveField field) => userValues.GetValueOrDefault(field);

    /// <summary>
    /// Gets the AI layer's value of a field: the value of the newest active, not rejected run that gives one. It is
    /// returned even when a user value is effective, so the two can be compared.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The AI value, or <c>null</c>.</returns>
    public AiFieldValue? GetLatestAiValue(DescriptiveField field) =>
        NewestFirst(runs.Where(run => run.ProvidesAiLayer))
            .Select(run => aiValues.FirstOrDefault(value => value.RunId == run.Id && value.Field == field))
            .FirstOrDefault(value => value is not null);

    /// <summary>
    /// Gets the technical layer's value of a field, read from the file.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The technical value, or <c>null</c>.</returns>
    public TechnicalFieldValue? GetTechnicalValue(DescriptiveField field) => technicalValues.GetValueOrDefault(field);

    /// <summary>
    /// Gets the new AI suggestion flagged on a field's user value (D020): the value the suggesting run gave.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The suggested value, or <c>null</c> when the field has no suggestion.</returns>
    public AiFieldValue? GetSuggestedValue(DescriptiveField field) =>
        userValues.TryGetValue(field, out var user) && user.SuggestionRunId is { } runId
            ? aiValues.FirstOrDefault(value => value.RunId == runId && value.Field == field)
            : null;

    /// <summary>
    /// Gets the new AI suggestion on a tag, if it has one.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>The suggestion, or <c>null</c>.</returns>
    public TagSuggestion? GetTagSuggestion(TagName tag) => tagSuggestions.GetValueOrDefault(tag);

    /// <summary>
    /// Resolves the effective value of a field: User, then AI, then Technical.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <returns>The effective value with its provenance, or <c>null</c> if no layer has one.</returns>
    public ResolvedField? GetEffectiveValue(DescriptiveField field)
    {
        if (userValues.TryGetValue(field, out var user))
        {
            return new ResolvedField(
                field,
                user.Value,
                MetadataSource.User,
                user.Origin,
                user.AcceptedFrom?.RunId,
                user.AcceptedFrom?.ModelId,
                null,
                user.UpdatedAt,
                user.SuggestionRunId);
        }

        if (GetLatestAiValue(field) is { } ai)
        {
            return new ResolvedField(field, ai.Value, MetadataSource.AI, null, ai.RunId, ai.ModelId, ai.Confidence, ai.CreatedAt, null);
        }

        if (technicalValues.TryGetValue(field, out var technical))
        {
            return new ResolvedField(field, technical.Value, MetadataSource.Technical, null, null, null, null, technical.ReadAt, null);
        }

        return null;
    }

    public IReadOnlyList<ResolvedField> GetEffectiveValues() =>
        [.. DescriptiveFields.All.Select(GetEffectiveValue).OfType<ResolvedField>()];

    /// <summary>
    /// Resolves the effective tags. A tag appears once, from the strongest source: User, then AI, then Technical.
    /// AI and technical assignments of rejected tags are left out.
    /// </summary>
    /// <returns>The effective tags.</returns>
    public IReadOnlyList<ResolvedTag> GetEffectiveTags()
    {
        var result = new Dictionary<TagName, ResolvedTag>();

        foreach (var tag in tags.Where(tag => tag.Source == MetadataSource.User))
        {
            result.TryAdd(
                tag.Tag,
                WithOtherForm(new ResolvedTag(tag.Tag, MetadataSource.User, tag.Origin, tag.AcceptedFrom?.RunId, tag.AcceptedFrom?.ModelId, null), tag));
        }

        foreach (var run in NewestFirst(runs.Where(run => run.ProvidesAiLayer)))
        {
            foreach (var tag in tags.Where(tag => tag.RunId == run.Id && !rejections.ContainsKey(tag.Tag)))
            {
                result.TryAdd(tag.Tag, WithOtherForm(new ResolvedTag(tag.Tag, MetadataSource.AI, null, tag.RunId, tag.ModelId, tag.Confidence), tag));
            }
        }

        foreach (var tag in tags.Where(tag => tag.Source == MetadataSource.Technical && !rejections.ContainsKey(tag.Tag)))
        {
            result.TryAdd(tag.Tag, WithOtherForm(new ResolvedTag(tag.Tag, MetadataSource.Technical, null, null, null, null), tag));
        }

        return [.. result.Values];

        static ResolvedTag WithOtherForm(ResolvedTag resolved, TagAssignment assignment) =>
            resolved with { OtherForm = assignment.OtherForm, IsOtherFormDerived = assignment.IsOtherFormDerived };
    }

    /// <summary>
    /// Replaces the descriptive values read from the file.
    /// </summary>
    /// <param name="values">The new technical values.</param>
    public void SetTechnicalValues(IEnumerable<TechnicalFieldValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        technicalValues.Clear();
        foreach (var value in values)
        {
            EnsureItem(value.ItemId);
            technicalValues[value.Field] = value;
        }
    }

    /// <summary>
    /// Replaces the tags read from the file, e.g. embedded keywords.
    /// </summary>
    /// <param name="names">The new technical tags.</param>
    /// <param name="now">Current time.</param>
    public void SetTechnicalTags(IEnumerable<TagName> names, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(names);

        tags.RemoveAll(tag => tag.Source == MetadataSource.Technical);
        tags.AddRange(names.Distinct().Select(name => TagAssignment.Technical(ItemId, name, now)));
    }

    /// <summary>
    /// Stores a value the user wrote or changed. It replaces any earlier user value and clears its suggestion.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="value">The value.</param>
    /// <param name="now">Current time.</param>
    public void EditValue(DescriptiveField field, FieldValue value, DateTimeOffset now) =>
        userValues[field] = UserFieldValue.Edited(ItemId, field, value, now);

    /// <summary>
    /// Confirms the current AI value of a field as-is. It becomes user-owned, with a reference to its run.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">The field has no effective AI value.</exception>
    public void AcceptAiValue(DescriptiveField field, DateTimeOffset now)
    {
        var ai = GetLatestAiValue(field) ?? throw new InvalidOperationException($"{field} has no AI value to accept.");

        userValues[field] = UserFieldValue.Accepted(ItemId, field, ai.Value, GetRun(ai.RunId).ToReference(), now);
    }

    /// <summary>
    /// Removes the user value of a field, so the AI or technical value becomes effective again. Only the user does this.
    /// </summary>
    /// <param name="field">The field.</param>
    public void ClearUserValue(DescriptiveField field) => userValues.Remove(field);

    /// <summary>
    /// Replaces a user value with the new AI suggestion flagged on it. The result is an accepted value from the suggesting run.
    /// </summary>
    /// <param name="field">The field.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">The field has no suggestion.</exception>
    public void AcceptSuggestion(DescriptiveField field, DateTimeOffset now)
    {
        if (!userValues.TryGetValue(field, out var user) || user.SuggestionRunId is not { } runId)
        {
            throw new InvalidOperationException($"{field} has no AI suggestion.");
        }

        var suggestion = aiValues.Single(value => value.RunId == runId && value.Field == field);
        userValues[field] = UserFieldValue.Accepted(ItemId, field, suggestion.Value, GetRun(runId).ToReference(), now);
    }

    /// <summary>
    /// Keeps the user value and clears its new AI suggestion.
    /// </summary>
    /// <param name="field">The field.</param>
    public void DismissSuggestion(DescriptiveField field)
    {
        if (userValues.TryGetValue(field, out var user))
        {
            userValues[field] = user.WithSuggestion(null);
        }
    }

    /// <summary>
    /// Adds a tag the user wrote. It removes the tag's tombstone, if there is one.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    public void AddTag(TagName tag, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);

        rejections.Remove(tag);
        if (!HasUserTag(tag))
        {
            tags.Add(TagAssignment.UserEdited(ItemId, tag, now));
            tagSuggestions.Remove(tag);
        }
    }

    /// <summary>
    /// Removes a user tag, edited or accepted, without a tombstone. If an AI run or the file gives the tag too, that
    /// assignment becomes effective again; <see cref="RejectTag"/> removes the tag for good.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <returns>Whether the item had the tag as a user tag.</returns>
    public bool RemoveTag(TagName tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (tags.RemoveAll(assignment => assignment.Source == MetadataSource.User && assignment.Tag == tag) == 0)
        {
            return false;
        }

        if (tagSuggestions.TryGetValue(tag, out var suggestion) && suggestion.Kind == TagSuggestionKind.Removed)
        {
            tagSuggestions.Remove(tag);
        }

        return true;
    }

    /// <summary>
    /// Confirms an AI tag of an active run. It becomes user-owned, with a reference to its run.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">No active run suggests the tag.</exception>
    public void AcceptTag(TagName tag, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);

        if (HasUserTag(tag))
        {
            return;
        }

        var (run, accepted) = NewestFirst(runs.Where(run => run.ProvidesAiLayer))
            .Select(run => (Run: run, Assignment: tags.FirstOrDefault(assignment => assignment.RunId == run.Id && assignment.Tag == tag)))
            .FirstOrDefault(pair => pair.Assignment is not null);
        if (run is null || accepted is null)
        {
            throw new InvalidOperationException($"No active run suggests the tag '{tag}'.");
        }

        rejections.Remove(tag);
        tags.Add(TagAssignment.UserAccepted(ItemId, tag, run.ToReference(), now, accepted));
        tagSuggestions.Remove(tag);
    }

    /// <summary>
    /// Removes a tag and leaves a tombstone, so neither the file nor any AI run can add it again.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    public void RejectTag(TagName tag, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);

        tags.RemoveAll(assignment => assignment.Source == MetadataSource.User && assignment.Tag == tag);
        var suggestingRun = NewestFirst(runs.Where(run => run.ProvidesAiLayer))
            .FirstOrDefault(run => tags.Any(assignment => assignment.RunId == run.Id && assignment.Tag == tag));
        rejections[tag] = new TagRejection(ItemId, tag, suggestingRun?.ToReference(), now);
        tagSuggestions.Remove(tag);
    }

    /// <summary>
    /// Follows a tag's new AI suggestion: an added tag is accepted, and an accepted tag the AI dropped is removed
    /// without a tombstone, since no run gives it any more.
    /// </summary>
    /// <param name="tag">The tag.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">The tag has no suggestion.</exception>
    public void AcceptTagSuggestion(TagName tag, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!tagSuggestions.TryGetValue(tag, out var suggestion))
        {
            throw new InvalidOperationException($"The tag '{tag}' has no AI suggestion.");
        }

        if (suggestion.Kind == TagSuggestionKind.Added)
        {
            AcceptTag(tag, now);
        }
        else
        {
            tags.RemoveAll(assignment => assignment.Source == MetadataSource.User && assignment.Tag == tag);
            tagSuggestions.Remove(tag);
        }
    }

    /// <summary>
    /// Keeps the tag as it is and clears its new AI suggestion.
    /// </summary>
    /// <param name="tag">The tag.</param>
    public void DismissTagSuggestion(TagName tag)
    {
        ArgumentNullException.ThrowIfNull(tag);

        tagSuggestions.Remove(tag);
    }

    /// <summary>
    /// Confirms every effective AI value and tag (D021 "Accept").
    /// </summary>
    /// <param name="now">Current time.</param>
    public void AcceptAll(DateTimeOffset now)
    {
        foreach (var field in GetEffectiveValues().Where(value => value.Source == MetadataSource.AI).Select(value => value.Field))
        {
            AcceptAiValue(field, now);
        }

        foreach (var tag in GetEffectiveTags().Where(tag => tag.Source == MetadataSource.AI).Select(tag => tag.Tag))
        {
            AcceptTag(tag, now);
        }
    }

    /// <summary>
    /// Applies a completed re-analysis: the run becomes its analyzer's active run, user values are flagged with
    /// differing suggestions, and runs beyond the retention are pruned.
    /// </summary>
    /// <param name="run">The completed run.</param>
    /// <param name="values">Field values the run produced.</param>
    /// <param name="runTags">AI tag assignments the run produced.</param>
    /// <param name="retainedPreviousRuns">How many previous runs of the analyzer to keep.</param>
    /// <returns>The pruned runs, and whether the effective values changed.</returns>
    public RunApplication ApplyRun(
        AnalysisRun run,
        IEnumerable<AiFieldValue> values,
        IEnumerable<TagAssignment> runTags,
        int retainedPreviousRuns)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(runTags);
        ArgumentOutOfRangeException.ThrowIfNegative(retainedPreviousRuns);
        EnsureItem(run.ItemId);
        if (run.Status != AnalysisRunStatus.Completed)
        {
            throw new InvalidOperationException($"Run {run.Id} isn't completed.");
        }

        if (runs.Any(existing => existing.Id == run.Id))
        {
            throw new InvalidOperationException($"Run {run.Id} is already applied.");
        }

        var newValues = values.ToList();
        var newTags = runTags.ToList();
        if (newValues.Any(value => value.RunId != run.Id) || newTags.Any(tag => tag.Source != MetadataSource.AI || tag.RunId != run.Id))
        {
            throw new ArgumentException($"Every value and tag must belong to run {run.Id}.", nameof(values));
        }

        var before = Snapshot();

        var previous = GetActiveRun(run.Analyzer);
        previous?.Deactivate();
        run.Activate();
        runs.Add(run);
        aiValues.AddRange(newValues);
        tags.AddRange(newTags);

        // A field is flagged when the new value differs from the user value and is new: the analyzer's previous run gave
        // something else, or the field's flag wasn't dealt with yet. So a dismissed suggestion stays dismissed until a
        // run produces a different value.
        foreach (var value in newValues)
        {
            if (userValues.TryGetValue(value.Field, out var user))
            {
                var previousValue = previous is null ? null : aiValues.FirstOrDefault(old => old.RunId == previous.Id && old.Field == value.Field);
                var isNew = previousValue is null || previousValue.Value != value.Value || user.SuggestionRunId is not null;
                userValues[value.Field] = user.WithSuggestion(value.Value == user.Value || !isNew ? null : run.Id);
            }
        }

        SuggestTags(run, previous, newTags);
        var pruned = Prune(run.Analyzer, retainedPreviousRuns);

        return new RunApplication(pruned, !SameAs(before, Snapshot()));
    }

    /// <summary>
    /// Rejects an analyzer's active run in review (D021). Its AI values and tags stop being effective; user values and
    /// embeddings are kept.
    /// </summary>
    /// <param name="analyzer">Analyzer key.</param>
    /// <param name="now">Current time.</param>
    /// <exception cref="InvalidOperationException">The analyzer has no active run.</exception>
    public void RejectActiveRun(string analyzer, DateTimeOffset now)
    {
        var run = GetActiveRun(analyzer) ?? throw new InvalidOperationException($"Analyzer {analyzer} has no active run.");

        run.Reject(now);
        ClearSuggestionsFrom([run.Id]);
    }

    private static IEnumerable<AnalysisRun> NewestFirst(IEnumerable<AnalysisRun> source) =>
        source.OrderByDescending(run => run.StartedAt).ThenByDescending(run => run.Id.Value);

    private static bool SameAs(
        (Dictionary<DescriptiveField, FieldValue> Values, HashSet<TagName> Tags) before,
        (Dictionary<DescriptiveField, FieldValue> Values, HashSet<TagName> Tags) after) =>
        before.Values.Count == after.Values.Count
        && before.Values.All(pair => after.Values.TryGetValue(pair.Key, out var value) && value == pair.Value)
        && before.Tags.SetEquals(after.Tags);

    private bool HasUserTag(TagName tag) =>
        tags.Any(assignment => assignment.Source == MetadataSource.User && assignment.Tag == tag);

    private AnalysisRun GetRun(Id<AnalysisRun> runId) => runs.Single(run => run.Id == runId);

    private List<Id<AnalysisRun>> Prune(string analyzer, int retainedPreviousRuns)
    {
        var pruned = NewestFirst(runs.Where(run => !run.IsActive && run.Analyzer == analyzer))
            .Skip(retainedPreviousRuns)
            .Select(run => run.Id)
            .ToList();

        runs.RemoveAll(run => pruned.Contains(run.Id));
        aiValues.RemoveAll(value => pruned.Contains(value.RunId));
        tags.RemoveAll(tag => tag.RunId is { } runId && pruned.Contains(runId));
        ClearSuggestionsFrom(pruned);

        return pruned;
    }

    // Re-evaluates the tag suggestions of the run's analyzer. An added tag counts only on a re-analysis (a first run
    // suggests nothing new), when the previous run didn't give it or it was flagged already and not dealt with.
    private void SuggestTags(AnalysisRun run, AnalysisRun? previous, List<TagAssignment> newTags)
    {
        var ofAnalyzer = tagSuggestions.Values.Where(suggestion => GetRun(suggestion.RunId).Analyzer == run.Analyzer).ToList();
        var stillAdded = ofAnalyzer.Where(suggestion => suggestion.Kind == TagSuggestionKind.Added).Select(suggestion => suggestion.Tag).ToHashSet();
        var stillRemoved = ofAnalyzer.Where(suggestion => suggestion.Kind == TagSuggestionKind.Removed).Select(suggestion => suggestion.Tag).ToHashSet();
        foreach (var suggestion in ofAnalyzer)
        {
            tagSuggestions.Remove(suggestion.Tag);
        }

        var given = newTags.Select(tag => tag.Tag).ToHashSet();
        var before = previous is null ? new HashSet<TagName>() : tags.Where(tag => tag.RunId == previous.Id).Select(tag => tag.Tag).ToHashSet();
        if (previous is not null)
        {
            foreach (var tag in given.Where(tag => !HasUserTag(tag) && !rejections.ContainsKey(tag)
                && (!before.Contains(tag) || stillAdded.Contains(tag))))
            {
                tagSuggestions[tag] = new TagSuggestion(ItemId, tag, run.Id, TagSuggestionKind.Added);
            }
        }

        // An accepted tag the run no longer gives is flagged only when that is new: the previous run still gave it, or
        // its flag wasn't dealt with yet. A dismissed Removed suggestion doesn't come back with every run.
        foreach (var accepted in tags.Where(tag => tag.Source == MetadataSource.User && tag.AcceptedFrom?.Analyzer == run.Analyzer
            && !given.Contains(tag.Tag) && (before.Contains(tag.Tag) || stillRemoved.Contains(tag.Tag))))
        {
            tagSuggestions[accepted.Tag] = new TagSuggestion(ItemId, accepted.Tag, run.Id, TagSuggestionKind.Removed);
        }
    }

    private void ClearSuggestionsFrom(IReadOnlyCollection<Id<AnalysisRun>> runIds)
    {
        foreach (var suggestion in tagSuggestions.Values.Where(suggestion => runIds.Contains(suggestion.RunId)).ToList())
        {
            tagSuggestions.Remove(suggestion.Tag);
        }

        foreach (var user in userValues.Values.ToList())
        {
            if (user.SuggestionRunId is { } runId && runIds.Contains(runId))
            {
                userValues[user.Field] = user.WithSuggestion(null);
            }
        }
    }

    private (Dictionary<DescriptiveField, FieldValue> Values, HashSet<TagName> Tags) Snapshot() =>
        (GetEffectiveValues().ToDictionary(value => value.Field, value => value.Value),
         GetEffectiveTags().Select(tag => tag.Tag).ToHashSet());

    private void EnsureItem(Id<MediaItem> itemId)
    {
        if (itemId != ItemId)
        {
            throw new ArgumentException($"Metadata of item {itemId} can't be added to item {ItemId}.", nameof(itemId));
        }
    }

    private void EnsureRetained(Id<AnalysisRun> runId)
    {
        if (!runs.Any(run => run.Id == runId))
        {
            throw new ArgumentException($"Run {runId} isn't among the retained runs.", nameof(runId));
        }
    }
}
