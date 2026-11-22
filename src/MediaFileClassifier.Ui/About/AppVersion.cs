using System.Reflection;

namespace MediaFileClassifier.Ui.About;

/// <summary>
/// The version and commit of the running build (MFC-1023). The version is the manual <c>&lt;Version&gt;</c> in
/// <c>Directory.Build.props</c>, and the SDK appends the commit SHA to the informational version as build
/// metadata (<c>1.0.0+abc1234…</c>).
/// </summary>
/// <param name="Version">SemVer version, e.g. <c>1.0.0</c> or <c>1.1.0-rc.1</c>, or <c>unknown</c>.</param>
/// <param name="Commit">Short commit SHA, or <c>null</c> when the build carries none.</param>
public sealed record AppVersion(string Version, string? Commit)
{
    private const int ShortCommitLength = 7;

    /// <summary>
    /// Gets the version of the running build, read from this assembly.
    /// </summary>
    public static AppVersion Current { get; } = FromAssembly(typeof(AppVersion).Assembly);

    /// <summary>
    /// Gets the text shown to people and written to the log: <c>1.0.0 (abc1234)</c>, or <c>1.0.0</c> without a commit.
    /// </summary>
    public string Text => Commit is null ? Version : $"{Version} ({Commit})";

    /// <summary>
    /// Reads the version of an assembly.
    /// </summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>Its version and commit.</returns>
    public static AppVersion FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
    }

    /// <summary>
    /// Splits an informational version into the version and the short commit.
    /// </summary>
    /// <param name="informationalVersion">E.g. <c>1.0.0+3bace9e0f1…</c>; <c>null</c> or blank gives <c>unknown</c>.</param>
    /// <returns>The version and commit.</returns>
    public static AppVersion Parse(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return new AppVersion("unknown", null);
        }

        var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        if (plus < 0)
        {
            return new AppVersion(informationalVersion.Trim(), null);
        }

        var version = informationalVersion[..plus].Trim();
        var commit = informationalVersion[(plus + 1)..].Trim();
        return new AppVersion(
            version.Length == 0 ? "unknown" : version,
            commit.Length == 0 ? null : commit[..Math.Min(commit.Length, ShortCommitLength)]);
    }
}
