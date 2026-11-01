using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Design;

namespace MediaFileClassifier.Infrastructure.DataAccess.Database;

/// <summary>
/// Lets <c>dotnet ef migrations add</c> create the context. See <c>docs/library-storage.md</c>.
/// </summary>
internal sealed class DesignTimeLibraryDbContextFactory : IDesignTimeDbContextFactory<LibraryDbContext>
{
    /// <inheritdoc />
    public LibraryDbContext CreateDbContext(string[] args) =>
        LibraryConnectionFactory.CreateContext(new SqliteConnection("Data Source=:memory:"), ownsConnection: true, tracking: true);
}
