using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.DataAccess.Database;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Round-trips every entity of the domain model (E01-S02) through a real library database (E01-S03-T05).
/// </summary>
public sealed class LibrarySchemaTests
{
    private static readonly DateTimeOffset Now = LibraryTestBed.Start;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryEntity_RoundTrips()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        item.SetHashes(new FileHashes { Fingerprint = UInt128.MaxValue, ContentHash = 42, PerceptualHash = ulong.MaxValue });
        var technical = new TechnicalMetadata
        {
            ItemId = item.Id,
            Width = 4000,
            Height = 3000,
            Orientation = ImageOrientation.Rotate90,
            Duration = TimeSpan.FromMilliseconds(12_345),
            FrameRate = 29.97,
            DateTaken = new DateTime(2024, 7, 14, 18, 30, 5, 250),
            DateTakenOffset = TimeSpan.FromHours(2),
            CameraModel = "X100V",
            Location = new GeoLocation(48.8584, 2.2945, 35),
            RawMetadataJson = """{"Exif":{}}""",
            ExtractedAt = Now,
        };
        var keyframe = new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.FromSeconds(1.5), 45);
        var embedding = Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [0.5f, -0.25f, 0.125f, 1f], Now, keyframe.Id);
        var collection = Collection.Create("Trips", Now);
        var membership = new CollectionMembership(collection.Id, item.Id, Now);
        var search = new SavedSearch { Id = Id<SavedSearch>.New(), Name = "Beaches", QueryText = "beach", Sort = SortMode.Date, CreatedAt = Now };
        var job = new ProcessingJob
        {
            Id = Id<ProcessingJob>.New(),
            Type = "analyze",
            Description = "Analyze 2 items",
            ItemIds = [item.Id, Id<MediaItem>.New()],
            State = ProcessingJobState.Paused,
            CreatedAt = Now,
        };

        await using (var db = await CreateContextAsync(bed, tracking: true))
        {
            db.AddRange(item, technical, keyframe, embedding, collection, membership, search, job);
            await db.SaveChangesAsync(Token);
        }

        await using (var db = await CreateContextAsync(bed, tracking: false))
        {
            var loadedItem = await db.MediaItems.SingleAsync(Token);
            Assert.Equal(item.Path, loadedItem.Path);
            Assert.Equal(item.Hashes, loadedItem.Hashes);
            Assert.Equal(item.FileCreatedAt, loadedItem.FileCreatedAt);

            Assert.Equivalent(technical, await db.TechnicalMetadata.SingleAsync(Token));
            Assert.Equal(keyframe, await db.Keyframes.SingleAsync(Token));

            var loadedEmbedding = await db.Embeddings.SingleAsync(Token);
            Assert.Equal(embedding.Vector.ToArray(), loadedEmbedding.Vector.ToArray());
            Assert.Equal(keyframe.Id, loadedEmbedding.KeyframeId);

            Assert.Equal(collection.Name, (await db.Collections.SingleAsync(Token)).Name);
            Assert.Equal(membership, await db.CollectionMemberships.SingleAsync(Token));
            Assert.Equal(search, await db.SavedSearches.SingleAsync(Token));
            var loadedJob = await db.ProcessingJobs.SingleAsync(Token);
            Assert.Equal(job.ItemIds, loadedJob.ItemIds);
            Assert.Equal(job.State, loadedJob.State);

            Assert.Equivalent(bed.Folder.Rules, (await db.Folders.SingleAsync(Token)).Rules);
            Assert.Equal(bed.Model.EmbeddingDimension, (await db.Models.SingleAsync(Token)).EmbeddingDimension);
        }
    }

    [Fact]
    public async Task Embedding_IsStoredAsHalfPrecisionBlob()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);
        await using (var db = await CreateContextAsync(bed, tracking: true))
        {
            db.Embeddings.Add(Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [1f, 0f, 0f, 0f], Now));
            await db.SaveChangesAsync(Token);
        }

        await using var connection = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT vector FROM embedding";
        var blob = (byte[])(await command.ExecuteScalarAsync(Token))!;

        // Two bytes per dimension, little-endian: 1.0 in half precision is 0x3C00 (D067).
        Assert.Equal(new byte[] { 0x00, 0x3C, 0, 0, 0, 0, 0, 0 }, blob);
    }

    [Fact]
    public async Task IdsAndTimes_AreStoredAsTextAndMilliseconds()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        await bed.Library.Items.AddAsync(item, Token);

        await using var connection = await new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).OpenAsync(Token);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, imported_at, hashes_fingerprint FROM media_item";
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));

        Assert.Equal(item.Id.Value.ToString(), reader.GetString(0));
        Assert.Equal(Now.ToUnixTimeMilliseconds(), reader.GetInt64(1));
        Assert.Equal(((UInt128)7919).ToString("x32", System.Globalization.CultureInfo.InvariantCulture), reader.GetString(2));
    }

    [Fact]
    public async Task DeletingItem_CascadesToEverythingItOwns()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var item = bed.NewItem(1);
        var collection = Collection.Create("Trips", Now);
        await using (var db = await CreateContextAsync(bed, tracking: true))
        {
            var keyframe = new Keyframe(Id<Keyframe>.New(), item.Id, 0, TimeSpan.Zero, null);
            db.AddRange(item, keyframe, collection, new CollectionMembership(collection.Id, item.Id, Now));
            db.Add(new TechnicalMetadata { ItemId = item.Id, ExtractedAt = Now });
            db.Add(Embedding.Create(item.Id, bed.Model, EmbeddingKind.Image, [1f, 0f, 0f, 0f], Now, keyframe.Id));
            await db.SaveChangesAsync(Token);
        }

        await bed.Library.Metadata.UpdateAsync(item.Id, metadata => metadata.AddTag(TagName.From("beach"), Now), Token);

        Assert.True(await bed.Library.Items.DeleteAsync(item.Id, Token));

        await using (var db = await CreateContextAsync(bed, tracking: false))
        {
            Assert.Empty(await db.Keyframes.ToListAsync(Token));
            Assert.Empty(await db.Embeddings.ToListAsync(Token));
            Assert.Empty(await db.TechnicalMetadata.ToListAsync(Token));
            Assert.Empty(await db.CollectionMemberships.ToListAsync(Token));
            Assert.Empty(await db.TagAssignments.ToListAsync(Token));
            Assert.Single(await db.Collections.ToListAsync(Token));
        }
    }

    private static Task<LibraryDbContext> CreateContextAsync(LibraryTestBed bed, bool tracking) =>
        new LibraryConnectionFactory(bed.Library.Layout.DatabasePath).CreateContextAsync(tracking, Token);
}
