using System.Reflection;
using Xunit.Sdk;
using Xunit.v3;

namespace MediaFileClassifier.Ui.Tests;

/// <summary>
/// Guards the test harness settings that keep the headless UI tests stable (MFC-984).
/// </summary>
public sealed class TestAppTests
{
    [Fact]
    public void Assembly_RunsTestsOneAtATime()
    {
        var parallelization = typeof(TestApp).Assembly.GetCustomAttribute<ParallelizationAttribute>();

        Assert.NotNull(parallelization);
        Assert.Equal(ParallelMode.None, parallelization.GetMode());
    }
}
