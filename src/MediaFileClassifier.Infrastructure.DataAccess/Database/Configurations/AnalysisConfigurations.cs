using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="AnalysisRun"/>.
/// </summary>
internal sealed class AnalysisRunConfiguration : IEntityTypeConfiguration<AnalysisRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AnalysisRun> builder)
    {
        builder.HasKey(run => run.Id);
        builder.Property(run => run.Id);
        builder.Property(run => run.ItemId);
        builder.Property(run => run.Analyzer);
        builder.Property(run => run.ProviderId);
        builder.Property(run => run.ModelId);
        builder.Property(run => run.ModelVersion);
        builder.Property(run => run.PromptVersion);
        builder.Property(run => run.StartedAt);
        builder.Property(run => run.CompletedAt);
        builder.Property(run => run.Status);
        builder.Property(run => run.Error);
        builder.Property(run => run.IsActive);
        builder.Property(run => run.RejectedAt);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(run => run.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ModelRegistration>().WithMany().HasForeignKey(run => run.ModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(run => new { run.ItemId, run.Analyzer });
    }
}

/// <summary>
/// Maps <see cref="ModelRegistration"/>.
/// </summary>
internal sealed class ModelRegistrationConfiguration : IEntityTypeConfiguration<ModelRegistration>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ModelRegistration> builder)
    {
        builder.HasKey(model => model.Id);
        builder.Property(model => model.Id);
        builder.Property(model => model.ProviderId);
        builder.Property(model => model.ModelName);
        builder.Property(model => model.Version);
        builder.Property(model => model.FileHash);
        builder.Property(model => model.EmbeddingDimension);
        builder.Property(model => model.RegisteredAt);
    }
}

/// <summary>
/// Maps <see cref="Embedding"/>: one row per vector, holding a little-endian half-precision BLOB (D067). Vectors of
/// one model are loaded together into the in-memory index, hence the index on the model.
/// </summary>
internal sealed class EmbeddingConfiguration : IEntityTypeConfiguration<Embedding>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Embedding> builder)
    {
        builder.HasKey(embedding => embedding.Id);
        builder.Property(embedding => embedding.Id);
        builder.Property(embedding => embedding.ItemId);
        builder.Property(embedding => embedding.ModelId);
        builder.Property(embedding => embedding.Kind);
        builder.Property(embedding => embedding.KeyframeId);
        builder.Property(embedding => embedding.Dimension);
        builder.Property(embedding => embedding.Vector)
            .HasConversion(new HalfVectorConverter(), new VectorComparer());
        builder.Property(embedding => embedding.CreatedAt);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(embedding => embedding.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ModelRegistration>().WithMany().HasForeignKey(embedding => embedding.ModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Keyframe>().WithMany().HasForeignKey(embedding => embedding.KeyframeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(embedding => new { embedding.ModelId, embedding.ItemId });
    }
}
