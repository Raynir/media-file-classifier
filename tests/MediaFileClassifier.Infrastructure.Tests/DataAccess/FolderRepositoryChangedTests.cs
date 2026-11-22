using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Infrastructure.Tests.DataAccess;

/// <summary>
/// Tests that the folder repository raises <c>Changed</c> after every write to the folder list (E04-S01-T01), so the
/// folder watchers follow it.
/// </summary>
public sealed class FolderRepositoryChangedTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Writes_AddUpdateAndDelete_RaiseChanged()
    {
        await using var bed = await LibraryTestBed.CreateAsync();
        var folders = bed.Library.Folders;
        var raised = 0;
        folders.Changed += (_, _) => raised++;
        var folder = IndexedFolder.Create(bed.Library.Library.Id, @"C:\Other", @"C:\Other", LibraryTestBed.Start);

        await folders.AddAsync(folder, Token);
        Assert.Equal(1, raised);

        folder.ChangeRules(new FolderRules { ExcludePatterns = ["raw"] });
        await folders.UpdateAsync(folder, Token);
        Assert.Equal(2, raised);

        await folders.DeleteAsync(folder.Id, Token);
        Assert.Equal(3, raised);

        // Nothing deleted, nothing to tell.
        await folders.DeleteAsync(folder.Id, Token);
        Assert.Equal(3, raised);
    }
}
