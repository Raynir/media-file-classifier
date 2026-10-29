using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Tests.Identity;

/// <summary>
/// Tests for <see cref="Id{TEntity}"/>.
/// </summary>
public sealed class IdTests
{
    [Fact]
    public void New_CreatesVersion7Uuid()
    {
        var id = Id<IdTests>.New();

        Assert.False(id.IsEmpty);
        Assert.Equal(7, id.Value.Version);
    }

    [Fact]
    public void New_IsTimeSortable()
    {
        var first = Id<IdTests>.New();
        Thread.Sleep(2);
        var second = Id<IdTests>.New();

        Assert.True(first.Value.CompareTo(second.Value) < 0);
    }

    [Fact]
    public void Parse_RoundTripsToString()
    {
        var id = Id<IdTests>.New();

        Assert.Equal(id, Id<IdTests>.Parse(id.ToString()));
    }

    [Fact]
    public void Default_IsEmpty()
    {
        Assert.True(default(Id<IdTests>).IsEmpty);
    }
}
