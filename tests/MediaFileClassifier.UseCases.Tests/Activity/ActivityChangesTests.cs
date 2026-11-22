using MediaFileClassifier.UseCases.Activity;

namespace MediaFileClassifier.UseCases.Tests.Activity;

/// <summary>
/// Tests for <see cref="ActivityChanges"/> (E15-S04): a subscriber's failure never reaches the code that recorded the event.
/// </summary>
public sealed class ActivityChangesTests
{
    [Fact]
    public void Raise_WithoutSubscribers_DoesNothing()
    {
        var changes = new ActivityChanges();

        changes.Raise();

        Assert.NotNull(changes);
    }

    [Fact]
    public void Raise_CallsEverySubscriber_EvenWhenOneThrows()
    {
        var changes = new ActivityChanges();
        var calls = new List<string>();
        changes.Changed += (_, _) => calls.Add("first");
        changes.Changed += (_, _) => throw new InvalidOperationException("A subscriber failed.");
        changes.Changed += (_, _) => calls.Add("third");

        changes.Raise();

        Assert.Equal(["first", "third"], calls);
    }
}
