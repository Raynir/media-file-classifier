using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;

namespace MediaFileClassifier.Domain.Tests.Metadata;

/// <summary>
/// Builds item metadata and analysis runs for tests, with a clock that moves forward on every run.
/// </summary>
internal sealed class ItemMetadataFixture
{
    public const string Caption = "caption";

    private readonly ModelRegistration model = ModelRegistration.Create("onnx", "florence-2", "1.0", null, null, Start);

    public ItemMetadataFixture() => Metadata = new ItemMetadata(ItemId);

    public static DateTimeOffset Start { get; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public Id<MediaItem> ItemId { get; } = Id<MediaItem>.New();

    public ItemMetadata Metadata { get; }

    public DateTimeOffset Now { get; private set; } = Start;

    /// <summary>
    /// Runs the analyzer and applies the result, as re-analysis does.
    /// </summary>
    /// <param name="title">AI title, or <c>null</c> for none.</param>
    /// <param name="tags">AI tags.</param>
    /// <param name="retainedPreviousRuns">Runs to keep besides the active one.</param>
    /// <param name="analyzer">Analyzer key.</param>
    /// <returns>The run and the outcome.</returns>
    public (AnalysisRun Run, RunApplication Result) Analyze(
        string? title,
        string[]? tags = null,
        int retainedPreviousRuns = 1,
        string analyzer = Caption)
    {
        Now = Now.AddMinutes(1);
        var run = AnalysisRun.Start(ItemId, analyzer, model, "v1", Now);
        run.Complete(Now);

        var values = title is null ? [] : new[] { AiFieldValue.For(run, DescriptiveField.Title, FieldValue.FromText(title), 0.9, Now) };
        var runTags = (tags ?? []).Select(tag => TagAssignment.Ai(run, TagName.From(tag), 0.8, Now));

        return (run, Metadata.ApplyRun(run, values, runTags, retainedPreviousRuns));
    }

    public DateTimeOffset Tick()
    {
        Now = Now.AddMinutes(1);
        return Now;
    }

    public string? EffectiveTitle() => Metadata.GetEffectiveValue(DescriptiveField.Title)?.Value.Text;

    public string[] EffectiveTags() => [.. Metadata.GetEffectiveTags().Select(tag => tag.Tag.Value).Order(StringComparer.Ordinal)];
}
