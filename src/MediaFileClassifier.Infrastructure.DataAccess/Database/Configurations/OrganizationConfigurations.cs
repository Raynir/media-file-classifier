using MediaFileClassifier.Domain.Activity;
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

/// <summary>
/// Maps <see cref="ActivityEvent"/> (E15-S04). The folder and item it points at are plain columns, not foreign keys:
/// the log outlives them. Pages are read newest first, so time and ID have an index, and so does the type for the filter.
/// </summary>
internal sealed class ActivityEventConfiguration : IEntityTypeConfiguration<ActivityEvent>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ActivityEvent> builder)
    {
        builder.HasKey(activity => activity.Id);
        builder.Property(activity => activity.Id);
        builder.Property(activity => activity.Type);
        builder.Property(activity => activity.Severity);
        builder.Property(activity => activity.OccurredAt);
        builder.Property(activity => activity.Subject);
        builder.Property(activity => activity.FolderId);
        builder.Property(activity => activity.ItemId);
        builder.Property(activity => activity.Counts).HasConversion(new ActivityCountsConverter(), new ActivityCountsComparer());
        builder.HasIndex(activity => new { activity.OccurredAt, activity.Id });
        builder.HasIndex(activity => new { activity.Type, activity.OccurredAt });
    }
}
