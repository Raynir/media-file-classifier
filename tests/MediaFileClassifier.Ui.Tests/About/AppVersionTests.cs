using MediaFileClassifier.Ui.About;

namespace MediaFileClassifier.Ui.Tests.About;

/// <summary>
/// Tests for the version text (MFC-1023).
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData("1.0.0+3bace9e0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6", "1.0.0", "3bace9e", "1.0.0 (3bace9e)")]
    [InlineData("1.1.0-rc.1+abc", "1.1.0-rc.1", "abc", "1.1.0-rc.1 (abc)")]
    [InlineData("1.0.0", "1.0.0", null, "1.0.0")]
    [InlineData("1.0.0+", "1.0.0", null, "1.0.0")]
    public void Parse_SplitsTheVersionFromTheShortCommit(string informational, string version, string? commit, string text)
    {
        var parsed = AppVersion.Parse(informational);

        Assert.Equal(version, parsed.Version);
        Assert.Equal(commit, parsed.Commit);
        Assert.Equal(text, parsed.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("+abc")]
    public void Parse_WithoutAVersion_SaysUnknown(string? informational)
        => Assert.Equal("unknown", AppVersion.Parse(informational).Version);

    [Fact]
    public void Current_IsTheBuildsVersion_WithTheCommit()
    {
        var current = AppVersion.Current;

        Assert.Matches(@"^\d+\.\d+\.\d+", current.Version);
        Assert.Equal(AppVersion.FromAssembly(typeof(AppVersion).Assembly), current);
    }
}
