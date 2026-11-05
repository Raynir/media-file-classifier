using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="MediaItem"/>. Hashes are columns of their own, so duplicate detection (E12) and rescans
/// (E01-S06) can look them up by index.
/// </summary>
internal sealed class MediaItemConfiguration : IEntityTypeConfiguration<MediaItem>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MediaItem> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id);
        builder.Property(item => item.FolderId);
        builder.Property(item => item.Path);
        builder.Property(item => item.MediaType);
        builder.Property(item => item.SizeBytes);
        builder.Property(item => item.FileCreatedAt);
        builder.Property(item => item.FileModifiedAt);
        builder.Property(item => item.ImportedAt);
        builder.Property(item => item.Status);
        builder.Property(item => item.StatusReason);
        builder.Property(item => item.StatusChangedAt);
        builder.Property(item => item.ReviewStatus);
        builder.Property(item => item.ReviewStatusChangedAt);
        builder.Property(item => item.IsFavorite);
        builder.Property(item => item.FavoriteChangedAt);
        builder.ComplexProperty(item => item.Hashes, hashes =>
        {
            hashes.Property(value => value.Fingerprint);
            hashes.Property(value => value.ContentHash);
            hashes.Property(value => value.PerceptualHash);
        });
        builder.ComplexProperty(item => item.Error, error =>
        {
            error.Property(value => value.Stage);
            error.Property(value => value.ExceptionType);
            error.Property(value => value.IsPermanent);
        });

        // Removing a folder's items is an explicit step (E02), never a side effect.
        builder.HasOne<IndexedFolder>().WithMany().HasForeignKey(item => item.FolderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(item => new { item.FolderId, item.Path }).IsUnique();
    }
}

/// <summary>
/// Maps <see cref="TechnicalMetadata"/>, one per item.
/// </summary>
internal sealed class TechnicalMetadataConfiguration : IEntityTypeConfiguration<TechnicalMetadata>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TechnicalMetadata> builder)
    {
        builder.HasKey(metadata => metadata.ItemId);
        builder.ComplexProperty(metadata => metadata.Location, location =>
        {
            location.Property(value => value.Latitude);
            location.Property(value => value.Longitude);
            location.Property(value => value.AltitudeMeters);
        });
        builder.Property(metadata => metadata.Warnings).HasConversion(new ExtractionWarningListConverter(), new ExtractionWarningListComparer());
        builder.HasOne<MediaItem>().WithOne().HasForeignKey<TechnicalMetadata>(metadata => metadata.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="Keyframe"/>.
/// </summary>
internal sealed class KeyframeConfiguration : IEntityTypeConfiguration<Keyframe>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Keyframe> builder)
    {
        builder.HasKey(keyframe => keyframe.Id);
        builder.Property(keyframe => keyframe.Id);
        builder.Property(keyframe => keyframe.ItemId);
        builder.Property(keyframe => keyframe.Index);
        builder.Property(keyframe => keyframe.Timestamp);
        builder.Property(keyframe => keyframe.FrameNumber);
        builder.Property(keyframe => keyframe.PerceptualHash);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(keyframe => keyframe.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(keyframe => new { keyframe.ItemId, keyframe.Index }).IsUnique();
    }
}
