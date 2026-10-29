using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Tests.Metadata;

namespace MediaFileClassifier.Domain.Tests.Analysis;

/// <summary>
/// Tests for analysis run activation and retention (D020).
/// </summary>
public sealed class AnalysisRunRetentionTests
{
    private readonly ItemMetadataFixture fixture = new();

    private ItemMetadata Metadata => fixture.Metadata;

    [Fact]
    public void NewRun_BecomesActive_AndPreviousRunIsKeptForComparison()
    {
        var (first, _) = fixture.Analyze("Dog on a beach", ["dog"]);
        var (second, _) = fixture.Analyze("Dog running", ["dog", "sand"]);

        Assert.Same(second, Metadata.GetActiveRun(ItemMetadataFixture.Caption));
        var previous = Assert.Single(Metadata.GetPreviousRuns(ItemMetadataFixture.Caption));
        Assert.Same(first, previous);
        Assert.False(previous.IsActive);
        Assert.Equal("Dog on a beach", Assert.Single(Metadata.GetRunValues(first.Id)).Value.Text);
        Assert.Single(Metadata.GetRunTags(first.Id));
    }

    [Fact]
    public void RunsBeyondRetention_ArePrunedWithTheirValuesAndTags()
    {
        var (first, _) = fixture.Analyze("One", ["a"]);
        fixture.Analyze("Two", ["b"]);

        var (_, result) = fixture.Analyze("Three", ["c"]);

        Assert.Equal([first.Id], result.PrunedRunIds);
        Assert.Equal(2, Metadata.Runs.Count);
        Assert.Empty(Metadata.GetRunValues(first.Id));
        Assert.Empty(Metadata.GetRunTags(first.Id));
    }

    [Fact]
    public void ZeroRetention_KeepsOnlyTheActiveRun()
    {
        fixture.Analyze("One", retainedPreviousRuns: 0);

        fixture.Analyze("Two", retainedPreviousRuns: 0);

        Assert.Single(Metadata.Runs);
        Assert.Empty(Metadata.GetPreviousRuns(ItemMetadataFixture.Caption));
    }

    [Fact]
    public void AcceptedValue_KeepsRunSnapshot_AfterItsRunIsPruned()
    {
        var (first, _) = fixture.Analyze("One");
        Metadata.AcceptAiValue(DescriptiveField.Title, fixture.Tick());

        fixture.Analyze("Two");
        fixture.Analyze("Three");

        Assert.DoesNotContain(Metadata.Runs, run => run.Id == first.Id);
        var user = Assert.Single(Metadata.UserValues);
        Assert.Equal("One", user.Value.Text);
        Assert.Equal(first.Id, user.AcceptedFrom?.RunId);
        Assert.Equal(first.ModelId, user.AcceptedFrom?.ModelId);
        Assert.Equal(first.StartedAt, user.AcceptedFrom?.RunAt);
    }

    [Fact]
    public void Suggestion_IsCleared_WhenItsRunIsPruned()
    {
        fixture.Analyze("One");
        Metadata.EditValue(DescriptiveField.Title, FieldValue.FromText("Mine"), fixture.Tick());
        var (suggesting, _) = fixture.Analyze("Two");
        fixture.Analyze(null);
        Assert.Equal(suggesting.Id, Assert.Single(Metadata.UserValues).SuggestionRunId);

        fixture.Analyze(null);

        Assert.False(Assert.Single(Metadata.UserValues).HasNewAiSuggestion);
    }

    [Fact]
    public void ApplyRun_RejectsRunThatIsNotCompleted()
    {
        var model = ModelRegistration.Create("onnx", "florence-2", "1.0", null, null, fixture.Now);
        var run = AnalysisRun.Start(fixture.ItemId, ItemMetadataFixture.Caption, model, null, fixture.Now);
        run.Fail("Model crashed", fixture.Tick());

        Assert.Throws<InvalidOperationException>(() => Metadata.ApplyRun(run, [], [], 1));
        Assert.Equal(AnalysisRunStatus.Failed, run.Status);
    }

    [Fact]
    public void ApplyRun_RejectsRunOfAnotherItem()
    {
        var model = ModelRegistration.Create("onnx", "florence-2", "1.0", null, null, fixture.Now);
        var run = AnalysisRun.Start(Id<MediaItem>.New(), ItemMetadataFixture.Caption, model, null, fixture.Now);
        run.Complete(fixture.Tick());

        Assert.Throws<ArgumentException>(() => Metadata.ApplyRun(run, [], [], 1));
    }

    [Fact]
    public void Run_CannotFinishTwice()
    {
        var model = ModelRegistration.Create("onnx", "florence-2", "1.0", null, null, fixture.Now);
        var run = AnalysisRun.Start(fixture.ItemId, ItemMetadataFixture.Caption, model, "v2", fixture.Now);
        run.Complete(fixture.Tick());

        Assert.Throws<InvalidOperationException>(() => run.Fail("late failure", fixture.Tick()));
        Assert.Equal("v2", run.PromptVersion);
        Assert.Equal("1.0", run.ModelVersion);
    }
}
