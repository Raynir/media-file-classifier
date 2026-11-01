using MediaFileClassifier.Evaluation.Data;

namespace MediaFileClassifier.Evaluation.Tests.Data;

/// <summary>
/// The data-consistency check of the committed public set in <c>eval/</c>. It needs no media files,
/// so it runs in CI on every build (E01-S10). The model run itself is manual for now.
/// </summary>
public sealed class PublicDataSetTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void PublicSet_IsValid()
    {
        var data = EvaluationData.Load(Path.Combine(RepositoryRoot, "eval"));

        Assert.False(data.Info.Private);
        Assert.Empty(DataValidator.Validate(data, checkFiles: false));
    }

    [Fact]
    public void PublicSet_CoversEveryMediaKindAndQueryCategory()
    {
        var data = EvaluationData.Load(Path.Combine(RepositoryRoot, "eval"));

        Assert.Equal(DataValidator.Kinds.Order(), data.Items.Select(i => i.Kind).Distinct().Order());
        foreach (var language in DataValidator.Languages)
        {
            Assert.Equal(
                QueryCategories.All.Order(),
                data.Queries.Where(q => q.Language == language).Select(q => q.Category).Distinct().Order());
        }
    }

    [Theory]
    [InlineData("eval/corpus/")]
    [InlineData("eval/private/")]
    public void PrivateAndFetchedFiles_AreGitIgnored(string pattern)
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot, ".gitignore"));

        Assert.Contains(pattern, lines);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MediaFileClassifier.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("The repository root was not found above the test binaries.");
    }
}
