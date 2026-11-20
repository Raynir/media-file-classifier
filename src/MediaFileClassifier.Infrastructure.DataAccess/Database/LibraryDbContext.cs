using System.Text;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Collections;
using MediaFileClassifier.Domain.Embeddings;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Domain.Searches;
using MediaFileClassifier.Infrastructure.DataAccess.Database.Conversions;
using Microsoft.EntityFrameworkCore;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// EF Core model of one library database (D071). The domain entities are mapped directly; tables and columns use
/// snake_case names; IDs are text, times are integer milliseconds. See <c>docs/library-storage.md</c>.
/// </summary>
/// <param name="options">Context options.</param>
internal sealed class LibraryDbContext(DbContextOptions<LibraryDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Name of the table that records the applied migrations, which is the schema version table.
    /// </summary>
    public const string SchemaVersionTable = "schema_version";

    public DbSet<Library> Libraries => Set<Library>();

    public DbSet<IndexedFolder> Folders => Set<IndexedFolder>();

    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    public DbSet<TechnicalMetadata> TechnicalMetadata => Set<TechnicalMetadata>();

    public DbSet<Keyframe> Keyframes => Set<Keyframe>();

    public DbSet<TechnicalFieldValue> TechnicalFieldValues => Set<TechnicalFieldValue>();

    public DbSet<UserFieldValue> UserFieldValues => Set<UserFieldValue>();

    public DbSet<AnalysisRun> AnalysisRuns => Set<AnalysisRun>();

    public DbSet<AiFieldValue> AiFieldValues => Set<AiFieldValue>();

    public DbSet<AnalyzerState> AnalyzerStates => Set<AnalyzerState>();

    public DbSet<TagAssignment> TagAssignments => Set<TagAssignment>();

    public DbSet<TagRejection> TagRejections => Set<TagRejection>();

    /// <summary>
    /// Gets the tag suggestions.
    /// </summary>
    public DbSet<TagSuggestion> TagSuggestions => Set<TagSuggestion>();

    public DbSet<ModelRegistration> Models => Set<ModelRegistration>();

    public DbSet<Embedding> Embeddings => Set<Embedding>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<CollectionMembership> CollectionMemberships => Set<CollectionMembership>();

    public DbSet<SavedSearch> SavedSearches => Set<SavedSearch>();

    public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();

    public DbSet<WorkItem> WorkItems => Set<WorkItem>();

    /// <summary>
    /// Converts a PascalCase name to snake_case, e.g. <c>AcceptedFrom_RunId</c> to <c>accepted_from_run_id</c>.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The snake_case name.</returns>
    public static string ToSnakeCase(string name)
    {
        var result = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c) && i > 0 && char.IsLetterOrDigit(name[i - 1])
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                result.Append('_');
            }

            result.Append(char.ToLowerInvariant(c));
        }

        return result.ToString();
    }

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<Id<Library>>().HaveConversion<IdConverter<Library>>();
        configurationBuilder.Properties<Id<IndexedFolder>>().HaveConversion<IdConverter<IndexedFolder>>();
        configurationBuilder.Properties<Id<MediaItem>>().HaveConversion<IdConverter<MediaItem>>();
        configurationBuilder.Properties<Id<Keyframe>>().HaveConversion<IdConverter<Keyframe>>();
        configurationBuilder.Properties<Id<AnalysisRun>>().HaveConversion<IdConverter<AnalysisRun>>();
        configurationBuilder.Properties<Id<ModelRegistration>>().HaveConversion<IdConverter<ModelRegistration>>();
        configurationBuilder.Properties<Id<Embedding>>().HaveConversion<IdConverter<Embedding>>();
        configurationBuilder.Properties<Id<Collection>>().HaveConversion<IdConverter<Collection>>();
        configurationBuilder.Properties<Id<SavedSearch>>().HaveConversion<IdConverter<SavedSearch>>();
        configurationBuilder.Properties<Id<ProcessingJob>>().HaveConversion<IdConverter<ProcessingJob>>();
        configurationBuilder.Properties<Id<WorkItem>>().HaveConversion<IdConverter<WorkItem>>();

        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UnixMillisecondsConverter>();
        configurationBuilder.Properties<DateTime>().HaveConversion<WallClockMillisecondsConverter>();
        configurationBuilder.Properties<TimeSpan>().HaveConversion<TimeSpanMillisecondsConverter>();
        configurationBuilder.Properties<UInt128>().HaveConversion<UInt128HexConverter>();
        configurationBuilder.Properties<ulong>().HaveConversion<UInt64BitsConverter>();

        configurationBuilder.Properties<RelativePath>().HaveConversion<RelativePathConverter>();
        configurationBuilder.Properties<TagName>().HaveConversion<TagNameConverter>();
        configurationBuilder.Properties<FieldValue>().HaveConversion<FieldValueConverter>();
        configurationBuilder.Properties<FolderRules>().HaveConversion<FolderRulesConverter>();
        configurationBuilder.Properties<LibrarySettings>().HaveConversion<LibrarySettingsConverter>();
    }

    /// <summary>
    /// Upper-cases a text in every alphabet, with the connection's <c>fold_case</c> function; only for queries.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The folded text.</returns>
    public static string FoldCase(string text) => text.ToUpperInvariant();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDbFunction(typeof(LibraryDbContext).GetMethod(nameof(FoldCase), [typeof(string)])!)
            .HasName(LibraryConnectionFactory.FoldCase);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LibraryDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            entityType.SetTableName(ToSnakeCase(entityType.ClrType.Name));
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var complexProperty in entityType.GetComplexProperties())
            {
                foreach (var property in complexProperty.ComplexType.GetProperties())
                {
                    property.SetColumnName(ToSnakeCase(complexProperty.Name + property.Name));
                }
            }
        }
    }
}
