using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.UseCases.Browsing.ItemDetails;
using MediaFileClassifier.UseCases.Browsing.ItemLocation;

namespace MediaFileClassifier.Infrastructure.Tests.Import;

/// <summary>
/// Tests for what the details panel shows about an item (E14-S05), over an imported library.
/// </summary>
public sealed class ItemDetailsTests : IAsyncLifetime
{
    private ImportTestBed bed = null!;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => bed = await ImportTestBed.CreateAsync();

    public async ValueTask DisposeAsync() => await bed.DisposeAsync();

    [Fact]
    public async Task Details_GiveTheFileTechnicalMetadataAndUserMetadata()
    {
        bed.Copy(Testing.Media.SampleMedia.JpegWithExif, "trip/photo.jpg");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var item = Assert.Single(await bed.ItemsAsync());
        var now = DateTimeOffset.UtcNow;
        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.EditValue(DescriptiveField.Description, FieldValue.FromText("Sunset"), now), Token);
        await bed.Library.Metadata.AddTagAsync([item.Id], TagName.From("beach"), Token);

        var details = await bed.Sender.Send(new GetItemDetailsQuery(item.Id), Token);

        Assert.NotNull(details);
        Assert.Equal(("photo.jpg", "jpg", MediaType.Image, item.SizeBytes), (details.FileName, details.FileFormat, details.MediaType, details.SizeBytes));
        Assert.Equal(ItemAvailability.Available, details.Location.Availability);
        Assert.Equal(Path.Combine(bed.Root, "trip", "photo.jpg"), details.Location.FullPath);
        Assert.NotNull(details.Technical?.Width);
        Assert.Equal("Sunset", details.Description);
        Assert.Null(details.Title);
        Assert.Equal(["beach"], details.Tags);
    }

    [Fact]
    public async Task Details_ForAMissingFile_SayItIsMissing_AndAnUnknownItemHasNone()
    {
        bed.Copy(Testing.Media.SampleMedia.PngWithAlpha, "gone.png");
        await bed.ImportAsync();
        await bed.WaitUntilIdleAsync();
        var item = Assert.Single(await bed.ItemsAsync());
        item.ChangeStatus(MediaItemStatus.Missing, DateTimeOffset.UtcNow);
        await bed.Library.Items.UpdateAsync(item, Token);

        var details = await bed.Sender.Send(new GetItemDetailsQuery(item.Id), Token);

        Assert.Equal(ItemAvailability.Missing, details?.Location.Availability);
        Assert.Null(await bed.Sender.Send(new GetItemDetailsQuery(Id<MediaItem>.New()), Token));
    }
}
