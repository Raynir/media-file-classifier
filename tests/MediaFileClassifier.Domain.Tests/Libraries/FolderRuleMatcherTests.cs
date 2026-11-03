using MediaFileClassifier.Domain.Libraries;

namespace MediaFileClassifier.Domain.Tests.Libraries;

/// <summary>
/// Tests for <see cref="FolderRuleMatcher"/>.
/// </summary>
public sealed class FolderRuleMatcherTests
{
    [Theory]
    [InlineData("a.jpg")]
    [InlineData("2024/trip/a.jpg")]
    public void NoPatterns_IncludeEverything(string path)
    {
        var matcher = new FolderRuleMatcher(new FolderRules());

        Assert.True(matcher.IsIncluded(path));
        Assert.True(matcher.ShouldEnterFolder("2024"));
    }

    [Theory]
    [InlineData("*.tmp", "a.tmp", true)]
    [InlineData("*.tmp", "deep/er/A.TMP", true)]
    [InlineData("*.tmp", "a.jpg", false)]
    [InlineData("Private", "Private/a.jpg", true)]
    [InlineData("Private", "2024/private/sub/a.jpg", true)]
    [InlineData("Private", "PrivateNot/a.jpg", false)]
    [InlineData("2024/raw", "2024/raw/a.jpg", true)]
    [InlineData("2024/raw", "old/2024/raw/a.jpg", false)]
    [InlineData("/2024/raw/", "2024/raw/a.jpg", true)]
    [InlineData(@"2024\raw", "2024/raw/a.jpg", true)]
    [InlineData("**/cache/**", "x/y/cache/z/a.jpg", true)]
    [InlineData("**/cache/**", "cache/a.jpg", true)]
    [InlineData("img_??.jpg", "IMG_01.jpg", true)]
    [InlineData("img_??.jpg", "img_001.jpg", false)]
    [InlineData("*copy*", "dir/photo copy 2.jpg", true)]
    public void ExcludePattern_ExcludesMatchingFilesAndFolderContents(string pattern, string path, bool excluded)
    {
        var matcher = new FolderRuleMatcher(new FolderRules { ExcludePatterns = [pattern] });

        Assert.Equal(!excluded, matcher.IsIncluded(path));
    }

    [Theory]
    [InlineData("Private", "Private", false)]
    [InlineData("Private", "2024/Private", false)]
    [InlineData("2024/raw", "2024", true)]
    [InlineData("2024/raw", "2024/raw", false)]
    [InlineData("*.tmp", "folder", true)]
    public void ExcludePattern_PrunesMatchingFolders(string pattern, string folder, bool entered)
    {
        var matcher = new FolderRuleMatcher(new FolderRules { ExcludePatterns = [pattern] });

        Assert.Equal(entered, matcher.ShouldEnterFolder(folder));
    }

    [Theory]
    [InlineData("a.jpg", true)]
    [InlineData("2024/a.png", false)]
    [InlineData("2024/a.JPG", true)]
    [InlineData("keep/a.png", true)]
    [InlineData("keep/sub/a.png", true)]
    public void IncludePatterns_LimitFilesToMatchesOrMatchingFolders(string path, bool included)
    {
        var matcher = new FolderRuleMatcher(new FolderRules { IncludePatterns = ["*.jpg", "keep"] });

        Assert.Equal(included, matcher.IsIncluded(path));
    }

    [Fact]
    public void Exclude_WinsOverInclude()
    {
        var matcher = new FolderRuleMatcher(new FolderRules { IncludePatterns = ["*.jpg"], ExcludePatterns = ["raw"] });

        Assert.False(matcher.IsIncluded("raw/a.jpg"));
        Assert.True(matcher.IsIncluded("cooked/a.jpg"));
    }

    [Fact]
    public void BlankPatterns_AreIgnored()
    {
        var matcher = new FolderRuleMatcher(new FolderRules { IncludePatterns = [" "], ExcludePatterns = [string.Empty, "/"] });

        Assert.True(matcher.IsIncluded("a.jpg"));
    }

    [Theory]
    [InlineData("/raw", "raw/a.jpg", false)]
    [InlineData("/raw", "2024/raw/a.jpg", true)]
    [InlineData("/2024/raw", "2024/raw/a.jpg", false)]
    [InlineData("raw", "2024/raw/a.jpg", false)]
    public void LeadingSlash_AnchorsAPatternAtTheRoot(string pattern, string path, bool included)
    {
        var matcher = new FolderRuleMatcher(new FolderRules { ExcludePatterns = [pattern] });

        Assert.Equal(included, matcher.IsIncluded(path));
    }

    [Fact]
    public void ForSubfolder_IsARootAnchoredPattern()
    {
        Assert.Equal("/2024/raw", FolderRules.ForSubfolder(@"2024\raw\"));
    }

    [Fact]
    public void ExcludePatternsUnder_ExcludeTheSameFilesInTheParent()
    {
        var child = new FolderRules { ExcludePatterns = ["/raw", "2024/tmp", "*.tmp", " "] };
        var parent = new FolderRuleMatcher(new FolderRules { ExcludePatterns = child.ExcludePatternsUnder("photos/child") });

        Assert.Equal(["/photos/child/raw", "/photos/child/2024/tmp", "/photos/child/**/*.tmp"], child.ExcludePatternsUnder("photos/child"));
        Assert.False(parent.IsIncluded("photos/child/raw/a.jpg"));
        Assert.False(parent.IsIncluded("photos/child/x/b.tmp"));
        Assert.True(parent.IsIncluded("photos/raw/a.jpg"));
        Assert.True(parent.IsIncluded("other/b.tmp"));
    }
}
