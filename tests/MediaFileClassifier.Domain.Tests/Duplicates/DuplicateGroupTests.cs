using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Tests.Duplicates;

/// <summary>
/// Tests for <see cref="DuplicateGroup"/> and <see cref="DuplicateGroupStateTransitions"/> (E12-S01-T01).
/// </summary>
public sealed class DuplicateGroupTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Start.AddMinutes(5);

    [Fact]
    public void CreateExact_IsOpenAndSuggested()
    {
        var group = DuplicateGroup.CreateExact(42, Start);

        Assert.Equal(DuplicateGroupType.Exact, group.Type);
        Assert.Equal(DuplicateGroupState.Open, group.State);
        Assert.Equal((UInt128)42, group.ContentHash);
        Assert.True(group.IsSuggested);
        Assert.Equal(Start, group.CreatedAt);
        Assert.Equal(Start, group.StateChangedAt);
    }

    [Fact]
    public void ExactGroup_WithoutContentHash_Throws() =>
        Assert.Throws<ArgumentException>(() => new DuplicateGroup(Id<DuplicateGroup>.New(), DuplicateGroupType.Exact, DuplicateGroupState.Open, null, Start, Start));

    [Theory]
    [InlineData(DuplicateGroupState.Open, DuplicateGroupState.Resolved, true)]
    [InlineData(DuplicateGroupState.Open, DuplicateGroupState.Ignored, true)]
    [InlineData(DuplicateGroupState.Resolved, DuplicateGroupState.Open, true)]
    [InlineData(DuplicateGroupState.Ignored, DuplicateGroupState.Open, true)]
    [InlineData(DuplicateGroupState.Resolved, DuplicateGroupState.Ignored, false)]
    [InlineData(DuplicateGroupState.Ignored, DuplicateGroupState.Resolved, false)]
    [InlineData(DuplicateGroupState.Ignored, DuplicateGroupState.Ignored, true)]
    public void IsAllowed_FollowsTheStateDiagram(DuplicateGroupState from, DuplicateGroupState to, bool allowed) =>
        Assert.Equal(allowed, DuplicateGroupStateTransitions.IsAllowed(from, to));

    [Theory]
    [InlineData(DuplicateGroupState.Resolved)]
    [InlineData(DuplicateGroupState.Ignored)]
    public void ChangeState_FromOpen_IsNoLongerSuggested(DuplicateGroupState state)
    {
        var group = DuplicateGroup.CreateExact(1, Start);

        Assert.True(group.ChangeState(state, Later));

        Assert.Equal(state, group.State);
        Assert.False(group.IsSuggested);
        Assert.Equal(Later, group.StateChangedAt);
    }

    [Fact]
    public void ChangeState_ToTheSameState_ChangesNothing()
    {
        var group = DuplicateGroup.CreateExact(1, Start);
        group.ChangeState(DuplicateGroupState.Ignored, Start);

        Assert.False(group.ChangeState(DuplicateGroupState.Ignored, Later));
        Assert.Equal(Start, group.StateChangedAt);
    }

    [Fact]
    public void ChangeState_IgnoredToResolved_Throws()
    {
        var group = DuplicateGroup.CreateExact(1, Start);
        group.ChangeState(DuplicateGroupState.Ignored, Start);

        Assert.Throws<InvalidOperationException>(() => group.ChangeState(DuplicateGroupState.Resolved, Later));
        Assert.Equal(DuplicateGroupState.Ignored, group.State);
    }

    [Theory]
    [InlineData(DuplicateGroupState.Resolved)]
    [InlineData(DuplicateGroupState.Ignored)]
    public void MemberJoined_ClosedGroup_OpensItAgain(DuplicateGroupState state)
    {
        var group = DuplicateGroup.CreateExact(1, Start);
        group.ChangeState(state, Start);

        Assert.True(group.MemberJoined(Later));

        Assert.True(group.IsSuggested);
        Assert.Equal(Later, group.StateChangedAt);
    }

    [Fact]
    public void MemberJoined_OpenGroup_StaysOpen()
    {
        var group = DuplicateGroup.CreateExact(1, Start);

        Assert.False(group.MemberJoined(Later));
        Assert.Equal(Start, group.StateChangedAt);
    }
}
