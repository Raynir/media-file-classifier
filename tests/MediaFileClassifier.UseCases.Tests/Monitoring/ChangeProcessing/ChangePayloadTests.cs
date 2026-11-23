using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.UseCases.Monitoring;
using MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

namespace MediaFileClassifier.UseCases.Tests.Monitoring.ChangeProcessing;

public sealed class ChangePayloadTests
{
    [Fact]
    public void Deserialize_WhatSerializeWrote_GivesTheSameChanges()
    {
        var modified = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
        FolderChange[] changes =
        [
            new(FolderChangeKind.Changed, "a/new.jpg", Stat: new MediaFileStat("a/new.jpg", 1234, modified) { CreatedUtc = modified.AddDays(-1) }),
            new(FolderChangeKind.Renamed, "b/moved.png", "a/moved.png", new MediaFileStat("b/moved.png", 99, modified)),
            new(FolderChangeKind.Deleted, "a/gone"),
        ];

        var read = ChangePayload.Deserialize(ChangePayload.Serialize(changes));

        Assert.Equal(changes, read);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Deserialize_NoPayload_GivesNoChanges(string? payload) => Assert.Empty(ChangePayload.Deserialize(payload));

    [Theory]
    [InlineData("""{"changes":[{"kind":"Exploded","path":"a.jpg"}]}""")]
    [InlineData("""{"changes":[{"kind":"7","path":"a.jpg"}]}""")]
    [InlineData("""{"changes":[{"kind":"Changed, Deleted","path":"a.jpg"}]}""")]
    [InlineData("""{"changes":[{"kind":"Renamed","oldPath":"a.jpg"}]}""")]
    [InlineData("""{"changes":[{"kind":"Changed","path":""}]}""")]
    public void Deserialize_UnknownKindOrNoPath_Throws(string payload) =>
        Assert.Throws<JsonException>(() => ChangePayload.Deserialize(payload));

    [Fact]
    public void Serialize_StoresTheKindByName_SoTheEnumCanGrow() =>
        Assert.Contains("\"kind\":\"Deleted\"", ChangePayload.Serialize([new FolderChange(FolderChangeKind.Deleted, "a.jpg")]), StringComparison.Ordinal);
}
