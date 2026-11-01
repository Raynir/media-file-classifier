using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Identity;

namespace MediaFileClassifier.Domain.Tests.Collections;

/// <summary>
/// Tests for <see cref="Collection"/>.
/// </summary>
public sealed class CollectionTests
{
    [Fact]
    public void Create_IsFlat()
    {
        var collection = Collection.Create(" Holidays ", DateTimeOffset.UnixEpoch);

        Assert.Null(collection.ParentId);
        Assert.Equal("Holidays", collection.Name);
    }

    [Fact]
    public void Collection_CannotBeItsOwnParent()
    {
        var id = Id<Collection>.New();

        Assert.Throws<ArgumentException>(() => new Collection(id, "Loop", id, DateTimeOffset.UnixEpoch));
    }
}
