using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

namespace MediaFileClassifier.UseCases.Tests.Monitoring.ChangeProcessing;

public sealed class HeldDeletesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TakeDue_ReturnsOnlyThePathsThatAreDue_AndKeepsTheRest()
    {
        var held = new HeldDeletes();
        var owner = new object();
        var folder = Id<IndexedFolder>.New();
        held.Hold(owner, folder, ["early.jpg"], Now);
        held.Hold(owner, folder, ["late.jpg"], Now.AddSeconds(30));

        var (taken, due) = held.TakeDue(Now);

        Assert.Same(owner, taken);
        var (folderId, paths) = Assert.Single(due);
        Assert.Equal(folder, folderId);
        Assert.Equal(["early.jpg"], paths);
        Assert.Equal([(folder, "late.jpg")], held.GetAll());
        Assert.Equal(Now.AddSeconds(30), held.NextDue());
    }

    [Fact]
    public void Hold_AgainForTheSamePath_MovesItsDueTimeOn()
    {
        var held = new HeldDeletes();
        var owner = new object();
        var folder = Id<IndexedFolder>.New();
        held.Hold(owner, folder, ["a.jpg"], Now);
        held.Hold(owner, folder, ["a.jpg"], Now.AddSeconds(30));

        Assert.Empty(held.TakeDue(Now).Due);
        Assert.Equal(Now.AddSeconds(30), held.NextDue());
    }

    [Fact]
    public void Hold_ForAnotherOwner_DropsWhatWasHeldForTheFirst()
    {
        var held = new HeldDeletes();
        var folder = Id<IndexedFolder>.New();
        held.Hold(new object(), folder, ["old-library.jpg"], Now);
        var second = new object();

        held.Hold(second, folder, ["new-library.jpg"], Now);

        Assert.Same(second, held.Owner);
        Assert.Equal([(folder, "new-library.jpg")], held.GetAll());
    }

    [Fact]
    public void NextDue_NothingHeld_IsNull() => Assert.Null(new HeldDeletes().NextDue());
}
