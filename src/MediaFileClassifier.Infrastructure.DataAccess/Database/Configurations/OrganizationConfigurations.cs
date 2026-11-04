using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="Collection"/>.
/// </summary>
internal sealed class CollectionConfiguration : IEntityTypeConfiguration<Collection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.HasKey(collection => collection.Id);
        builder.Property(collection => collection.Id);
        builder.Property(collection => collection.Name);
        builder.Property(collection => collection.ParentId);
        builder.Property(collection => collection.CreatedAt);
        builder.HasOne<Collection>().WithMany().HasForeignKey(collection => collection.ParentId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <see cref="CollectionMembership"/>, keyed by collection and item.
/// </summary>
internal sealed class CollectionMembershipConfiguration : IEntityTypeConfiguration<CollectionMembership>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CollectionMembership> builder)
    {
        builder.HasKey(membership => new { membership.CollectionId, membership.ItemId });
        builder.HasOne<Collection>().WithMany().HasForeignKey(membership => membership.CollectionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(membership => membership.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(membership => membership.ItemId);
    }
}

/// <summary>
/// Maps <see cref="SavedSearch"/>.
/// </summary>
internal sealed class SavedSearchConfiguration : IEntityTypeConfiguration<SavedSearch>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<SavedSearch> builder) => builder.HasKey(search => search.Id);
}

/// <summary>
/// Maps <see cref="ProcessingJob"/>. Its target items are a JSON array; the work itself is in <see cref="WorkItem"/> rows.
/// </summary>
internal sealed class ProcessingJobConfiguration : IEntityTypeConfiguration<ProcessingJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ProcessingJob> builder)
    {
        builder.HasKey(job => job.Id);
        builder.Property(job => job.ItemIds).HasConversion(new ItemIdListConverter(), new ItemIdListComparer());
        builder.HasOne<IndexedFolder>().WithMany().HasForeignKey(job => job.FolderId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(job => job.State);
    }
}

/// <summary>
/// Maps <see cref="WorkItem"/> (E05-S01-T01). The scheduler reads queued items by pool in priority order, so that
/// order has an index of its own.
/// </summary>
internal sealed class WorkItemConfiguration : IEntityTypeConfiguration<WorkItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<WorkItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.HasOne<ProcessingJob>().WithMany().HasForeignKey(item => item.JobId).OnDelete(DeleteBehavior.Cascade);

        // Deleting a media item drops the work still planned for it.
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(item => item.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(item => new { item.State, item.Pool, item.Priority, item.CreatedAt });
        builder.HasIndex(item => new { item.JobId, item.State });
        builder.HasIndex(item => item.ItemId);
    }
}
