using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Domain.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="DuplicateGroup"/> (E12-S01-T01). An exact group is found by its type and content hash, which is
/// unique among the groups that have one; the review lists groups by state.
/// </summary>
internal sealed class DuplicateGroupConfiguration : IEntityTypeConfiguration<DuplicateGroup>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DuplicateGroup> builder)
    {
        builder.HasKey(group => group.Id);
        builder.Property(group => group.Type);
        builder.Property(group => group.State);
        builder.Property(group => group.ContentHash);
        builder.Property(group => group.CreatedAt);
        builder.Property(group => group.StateChangedAt);
        builder.Ignore(group => group.IsSuggested);
        builder.HasIndex(group => new { group.Type, group.ContentHash }).IsUnique().HasFilter("\"content_hash\" IS NOT NULL");
        builder.HasIndex(group => group.State);
    }
}

/// <summary>
/// Maps <see cref="DuplicateGroupMember"/>, keyed by group and item. Deleting a group or an item deletes its memberships.
/// </summary>
internal sealed class DuplicateGroupMemberConfiguration : IEntityTypeConfiguration<DuplicateGroupMember>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<DuplicateGroupMember> builder)
    {
        builder.HasKey(member => new { member.GroupId, member.ItemId });
        builder.HasOne<DuplicateGroup>().WithMany().HasForeignKey(member => member.GroupId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(member => member.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(member => member.ItemId);
    }
}
