using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database.Configurations;

/// <summary>
/// Maps <see cref="TechnicalFieldValue"/>, keyed by item and field.
/// </summary>
internal sealed class TechnicalFieldValueConfiguration : IEntityTypeConfiguration<TechnicalFieldValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TechnicalFieldValue> builder)
    {
        builder.HasKey(value => new { value.ItemId, value.Field });
        builder.Property(value => value.ItemId);
        builder.Property(value => value.Field);
        builder.Property(value => value.Value);
        builder.Property(value => value.ReadAt);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="UserFieldValue"/>, keyed by item and field. The accepted-from run is a snapshot, so it outlives
/// the run; the suggesting run is a live reference.
/// </summary>
internal sealed class UserFieldValueConfiguration : IEntityTypeConfiguration<UserFieldValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<UserFieldValue> builder)
    {
        builder.HasKey(value => new { value.ItemId, value.Field });
        builder.Property(value => value.ItemId);
        builder.Property(value => value.Field);
        builder.Property(value => value.Value);
        builder.Property(value => value.Origin);
        builder.Property(value => value.UpdatedAt);
        builder.Property(value => value.SuggestionRunId);
        builder.ComplexProperty(value => value.AcceptedFrom, RunReferenceMapping.Configure);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AnalysisRun>().WithMany().HasForeignKey(value => value.SuggestionRunId).OnDelete(DeleteBehavior.SetNull);
    }
}

/// <summary>
/// Maps <see cref="AiFieldValue"/>, keyed by run and field. Values go with their run.
/// </summary>
internal sealed class AiFieldValueConfiguration : IEntityTypeConfiguration<AiFieldValue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<AiFieldValue> builder)
    {
        builder.HasKey(value => new { value.RunId, value.Field });
        builder.Property(value => value.RunId);
        builder.Property(value => value.ItemId);
        builder.Property(value => value.Field);
        builder.Property(value => value.Value);
        builder.Property(value => value.ModelId);
        builder.Property(value => value.Confidence);
        builder.Property(value => value.CreatedAt);
        builder.HasOne<AnalysisRun>().WithMany().HasForeignKey(value => value.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(value => value.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ModelRegistration>().WithMany().HasForeignKey(value => value.ModelId).OnDelete(DeleteBehavior.Restrict);
    }
}

/// <summary>
/// Maps <see cref="TagAssignment"/>. An assignment has no ID of its own, so the table has an integer row key.
/// </summary>
internal sealed class TagAssignmentConfiguration : IEntityTypeConfiguration<TagAssignment>
{
    /// <summary>
    /// Name of the shadow key property.
    /// </summary>
    public const string KeyProperty = "Id";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TagAssignment> builder)
    {
        builder.Property<long>(KeyProperty);
        builder.HasKey(KeyProperty);
        builder.Property(tag => tag.ItemId);
        builder.Property(tag => tag.Tag);
        builder.Property(tag => tag.Source);
        builder.Property(tag => tag.RunId);
        builder.Property(tag => tag.ModelId);
        builder.Property(tag => tag.Confidence);
        builder.Property(tag => tag.Origin);
        builder.Property(tag => tag.CreatedAt);
        builder.Property(tag => tag.OtherForm);
        builder.Property(tag => tag.IsOtherFormDerived);
        builder.Property<string>(TagKeyGenerator.Property).IsRequired().HasValueGenerator<TagKeyGenerator>();
        builder.Property<string>(OtherFormKeyGenerator.Property).HasValueGenerator<OtherFormKeyGenerator>();
        builder.ComplexProperty(tag => tag.AcceptedFrom, RunReferenceMapping.Configure);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(tag => tag.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AnalysisRun>().WithMany().HasForeignKey(tag => tag.RunId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<ModelRegistration>().WithMany().HasForeignKey(tag => tag.ModelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(TagKeyGenerator.Property);
        builder.HasIndex(OtherFormKeyGenerator.Property);
    }
}

/// <summary>
/// Maps <see cref="TagRejection"/>, keyed by item and tag key.
/// </summary>
internal sealed class TagRejectionConfiguration : IEntityTypeConfiguration<TagRejection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TagRejection> builder)
    {
        builder.Property(rejection => rejection.Tag);
        builder.Property<string>(TagKeyGenerator.Property).IsRequired().HasValueGenerator<TagKeyGenerator>();
        builder.HasKey(nameof(TagRejection.ItemId), TagKeyGenerator.Property);
        builder.ComplexProperty(rejection => rejection.RejectedFrom, RunReferenceMapping.Configure);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(rejection => rejection.ItemId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps <see cref="TagSuggestion"/>, keyed by item and tag key. A suggestion goes with its run.
/// </summary>
internal sealed class TagSuggestionConfiguration : IEntityTypeConfiguration<TagSuggestion>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TagSuggestion> builder)
    {
        builder.Property(suggestion => suggestion.Tag);
        builder.Property<string>(TagKeyGenerator.Property).IsRequired().HasValueGenerator<TagKeyGenerator>();
        builder.HasKey(nameof(TagSuggestion.ItemId), TagKeyGenerator.Property);
        builder.Property(suggestion => suggestion.Kind);
        builder.HasOne<MediaItem>().WithMany().HasForeignKey(suggestion => suggestion.ItemId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AnalysisRun>().WithMany().HasForeignKey(suggestion => suggestion.RunId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Maps a <see cref="RunReference"/> snapshot to columns of its owner.
/// </summary>
internal static class RunReferenceMapping
{
    /// <summary>
    /// Maps the snapshot's properties.
    /// </summary>
    /// <param name="builder">The complex property builder.</param>
    public static void Configure(ComplexPropertyBuilder<RunReference> builder)
    {
        builder.Property(run => run.RunId);
        builder.Property(run => run.Analyzer);
        builder.Property(run => run.ModelId);
        builder.Property(run => run.PromptVersion);
        builder.Property(run => run.RunAt);
    }
}
