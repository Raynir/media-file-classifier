using MediaFileClassifier.Domain.Libraries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="Library"/>. The database holds exactly one library record.
/// </summary>
internal sealed class LibraryConfiguration : IEntityTypeConfiguration<Library>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Library> builder)
    {
        builder.HasKey(library => library.Id);
        builder.Property(library => library.Id);
        builder.Property(library => library.Name);
        builder.Property(library => library.CreatedAt);
        builder.Property(library => library.ModifiedAt);
        builder.Property(library => library.SchemaVersion);
        builder.Property(library => library.Settings);
    }
}

/// <summary>
/// Maps <see cref="IndexedFolder"/>.
/// </summary>
internal sealed class IndexedFolderConfiguration : IEntityTypeConfiguration<IndexedFolder>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<IndexedFolder> builder)
    {
        builder.HasKey(folder => folder.Id);
        builder.Property(folder => folder.Id);
        builder.Property(folder => folder.LibraryId);
        builder.Property(folder => folder.DisplayPath);
        builder.Property(folder => folder.PlatformHandle);
        builder.Property(folder => folder.Rules);
        builder.Property(folder => folder.Status);
        builder.Property(folder => folder.AddedAt);
        builder.Property(folder => folder.StatusChangedAt);
        builder.Property(folder => folder.SelectedFilesOnly);
        builder.Property(folder => folder.VolumeLabel);
        builder.Property(folder => folder.VolumeId);
        builder.Property(folder => folder.MonitoringMode);
        builder.HasOne<Library>().WithMany().HasForeignKey(folder => folder.LibraryId).OnDelete(DeleteBehavior.Cascade);
    }
}
