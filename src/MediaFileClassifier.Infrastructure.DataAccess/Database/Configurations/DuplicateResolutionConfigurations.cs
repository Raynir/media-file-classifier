using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="DuplicateResolution"/> (E12-S04). The group and keeper are plain columns, not foreign keys: the log
/// outlives the group, which dissolves once the others are removed, and the items, which can be forgotten. Pages are read
/// newest first.
/// </summary>
internal sealed class DuplicateResolutionConfiguration : IEntityTypeConfiguration<DuplicateResolution>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DuplicateResolution> builder)
    {
        builder.HasKey(resolution => resolution.Id);
        builder.Property(resolution => resolution.GroupId);
        builder.Property(resolution => resolution.GroupType);
        builder.Property(resolution => resolution.Kind);
        builder.Property(resolution => resolution.KeeperId);
        builder.Property(resolution => resolution.MergedMetadata);
        builder.Property(resolution => resolution.RemovedFromLibrary);
        builder.Property(resolution => resolution.TrashRequested);
        builder.Property(resolution => resolution.CreatedAt);
        builder.Property(resolution => resolution.UndoneAt);
        builder.Property(resolution => resolution.Merge).HasConversion(new MergeRecordConverter(), new MergeRecordComparer());
        builder.Ignore(resolution => resolution.IsUndone);
        builder.HasIndex(resolution => new { resolution.CreatedAt, resolution.Id });
        builder.HasIndex(resolution => resolution.GroupId);
    }
}

/// <summary>
/// Maps <see cref="DuplicateResolutionMember"/>: deleted with its entry; the item is a plain column.
/// </summary>
internal sealed class DuplicateResolutionMemberConfiguration : IEntityTypeConfiguration<DuplicateResolutionMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DuplicateResolutionMember> builder)
    {
        builder.HasKey(member => new { member.ResolutionId, member.ItemId });
        builder.HasOne<DuplicateResolution>().WithMany().HasForeignKey(member => member.ResolutionId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(member => member.Path);
        builder.Property(member => member.Action);
        builder.Property(member => member.Reason);
        builder.HasIndex(member => member.ItemId);
    }
}
