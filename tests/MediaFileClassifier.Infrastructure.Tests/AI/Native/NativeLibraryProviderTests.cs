using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.Native;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Native;

/// <summary>
/// Tests for <see cref="NativeLibraryProvider"/> and <see cref="NativeLibraryLoader"/>, with a minimal working example
/// that loads a system library.
/// </summary>
public sealed class NativeLibraryProviderTests
{
    private static string SystemLibrary => OperatingSystem.IsWindows() ? "kernel32" : "libc.so.6";

    [Fact]
    public void TryLoad_SystemLibrary_Loads()
    {
        var result = NativeLibraryLoader.TryLoad(SystemLibrary, []);

        Assert.True(result.IsLoaded);
        Assert.NotEqual(IntPtr.Zero, result.Handle);
    }

    [Fact]
    public void TryLoad_Missing_ExplainsWithoutThrowing()
    {
        var result = NativeLibraryLoader.TryLoad("mfc-no-such-library", [Path.GetTempPath()]);

        Assert.False(result.IsLoaded);
        Assert.Contains("mfc-no-such-library", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void TryLoad_FileThatIsNotALibrary_ReportsTheFile()
    {
        var directory = Directory.CreateTempSubdirectory("mfc-tests-").FullName;
        try
        {
            var file = Path.Combine(directory, NativeLibraryLoader.GetCandidateFileNames("broken").Last());
            File.WriteAllText(file, "not a library");

            var result = NativeLibraryLoader.TryLoad("broken", [directory]);

            Assert.False(result.IsLoaded);
            Assert.Contains(file, result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Example_LibraryLoads_IsHealthyAndRuns()
    {
        var provider = new ExampleNativeOcr(SystemLibrary);

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);
        var output = await provider.RecognizeTextAsync(new AiImage(new byte[] { 1 }, "image/png"), TestContext.Current.CancellationToken);

        Assert.True(health.IsHealthy);
        Assert.Equal("loaded", output.Value.Text);
        Assert.Equal(provider.Model, output.Model);
    }

    [Fact]
    public async Task Example_LibraryMissing_ReportsNativeLibraryMissingAndThrowsOnUse()
    {
        var provider = new ExampleNativeOcr("mfc-no-such-library");

        var health = await provider.CheckHealthAsync(TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<AiProviderException>(
            () => provider.RecognizeTextAsync(new AiImage(new byte[] { 1 }, "image/png"), TestContext.Current.CancellationToken));

        Assert.Equal(ProviderHealthStatus.NativeLibraryMissing, health.Status);
        Assert.Contains("mfc-no-such-library", health.Message, StringComparison.Ordinal);
        Assert.Equal(AiFailureKind.ProviderError, exception.Kind);
    }

    /// <summary>
    /// The minimal native provider: it "recognizes" text by checking that its library handle is valid.
    /// </summary>
    private sealed class ExampleNativeOcr(string library)
        : NativeLibraryProvider("native:example", "Example", new ModelIdentity(ProviderIds.NativeLibrary, "example"), [library], []),
        IOcrProvider
    {
        public Task<AiOutput<OcrResult>> RecognizeTextAsync(AiImage image, CancellationToken cancellationToken)
        {
            return RunAsync(() => new OcrResult(GetLibraryHandle(library) != IntPtr.Zero ? "loaded" : string.Empty), cancellationToken);
        }
    }
}
