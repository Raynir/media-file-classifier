using MediaFileClassifier.UseCases.Monitoring;

namespace MediaFileClassifier.UseCases.Tests.Monitoring;

/// <summary>
/// Tests for <see cref="IgnoreList"/> (E04-S01-T03, DNEW-2): the default list catches temporary files, Office lock
/// files, partial downloads and editor swap files, and the list can be changed.
/// </summary>
public sealed class IgnoreListTests
{
    [Theory]
    [InlineData("~WRL0001.tmp")]
    [InlineData("~$Holiday.mp4")]
    [InlineData("Downloads/clip.mp4.crdownload")]
    [InlineData("clip.mp4.part")]
    [InlineData("clip.mp4.partial")]
    [InlineData("clip.mp4.download")]
    [InlineData(".clip.mp4.swp")]
    [InlineData("clip.mp4~")]
    [InlineData(".~lock.notes.odt#")]
    [InlineData("CLIP.MP4.TMP")]
    public void Default_IgnoresTemporaryAndPartialFiles(string path) =>
        Assert.True(new IgnoreList(MonitoringSettings.DefaultIgnorePatterns).IsIgnored(path));

    [Theory]
    [InlineData("clip.mp4")]
    [InlineData("2024/party.jpg")]
    [InlineData("tmp/party.jpg")]
    [InlineData("partner.jpg")]
    public void Default_KeepsMediaFiles(string path) =>
        Assert.False(new IgnoreList(MonitoringSettings.DefaultIgnorePatterns).IsIgnored(path));

    [Theory]
    [InlineData(@"temp\;1.jpg", "temp;1.jpg", true)]
    [InlineData(@"temp\;1.jpg", "1.jpg", false)]
    [InlineData(@"temp\;1.jpg", "temp", false)]
    [InlineData(@"*.bak; a\;b*.jpg", "a;b 2.jpg", true)]
    [InlineData(@"*.bak; a\;b*.jpg", "x.bak", true)]
    [InlineData(@"**/odd\;dir", "odd;dir/a.jpg", true)]
    public void EscapedSemicolon_IsPartOfThePattern(string patterns, string path, bool ignored) =>
        Assert.Equal(ignored, new IgnoreList(patterns).IsIgnored(path));

    [Fact]
    public void Custom_PatternsReplaceTheDefaults_AndBlankEntriesAreSkipped()
    {
        var list = new IgnoreList(" *.bak ;; **/drafts \n");

        Assert.True(list.IsIgnored("a.bak"));
        Assert.True(list.IsIgnored("drafts/a.jpg"));
        Assert.False(list.IsIgnored("a.tmp"));
    }

    [Theory]
    [InlineData("Old~/party.jpg")]
    [InlineData("session.part/clip.mp4")]
    [InlineData("a/b.tmp/c.jpg")]
    public void Default_FolderNamedLikeATemporaryFile_DoesntHideItsFiles(string path) =>
        Assert.False(new IgnoreList(MonitoringSettings.DefaultIgnorePatterns).IsIgnored(path));

    [Fact]
    public void Custom_PatternWithoutSlash_MatchesTheFileNameOnly()
    {
        var list = new IgnoreList("drafts");

        Assert.True(list.IsIgnored("a/drafts"));
        Assert.False(list.IsIgnored("drafts/a.jpg"));
    }

    [Fact]
    public void Empty_IgnoresNothing() => Assert.False(new IgnoreList(string.Empty).IsIgnored("a.tmp"));
}
