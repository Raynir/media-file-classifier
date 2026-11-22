using System.Reflection;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Jobs;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Processing;
using MediaFileClassifier.UseCases.Import;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Ui.Tests.Processing;

/// <summary>
/// Tests that every processing stage has a name in the resources, and what shows when one doesn't (MFC-1039).
/// </summary>
public sealed class StageNamesTests
{
    [Fact]
    public void EveryStageKey_HasAStageString()
    {
        var keys = StageKeys().ToList();
        Assert.Contains("import.fingerprint", keys);
        Assert.Contains("analysis.item", keys);
        Assert.Contains("rescan.check", keys);

        var missing = keys.Where(key => Localizer.Current.Find("Stage_" + key) is null).ToList();

        Assert.True(missing.Count == 0, "Add a Stage_<key> string to Strings.resx for: " + string.Join(", ", missing));
    }

    [Fact]
    public void CurrentWork_WithoutAStageString_ShowsTheJobDescription_NotTheKey()
    {
        var job = new JobProgress(Id<ProcessingJob>.New(), "test", "Analyze 20 items", [new StageProgress("no.such.stage.work", 237, 207, 1, false)], null);

        var work = new ProgressText(Localizer.Current).CurrentWork(job);

        Assert.Equal("Analyze 20 items: 208 of 237", work);
    }

    [Fact]
    public void Find_WithoutAStageString_ReturnsNullAndLogsOnce()
    {
        var logger = new RecordingLogger();
        var before = StageNames.Logger;
        StageNames.Logger = logger;
        try
        {
            Assert.Null(StageNames.Find(Localizer.Current, "no.such.stage.log"));
            Assert.Null(StageNames.Find(Localizer.Current, "no.such.stage.log"));
            Assert.Equal("Fingerprinting", StageNames.Find(Localizer.Current, "import.fingerprint"));
        }
        finally
        {
            StageNames.Logger = before;
        }

        var entry = Assert.Single(logger.Entries, e => e.Message.Contains("no.such.stage.log", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("Stage_no.such.stage.log", entry.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every stage key the use cases define: the constants of the <c>*Stages</c> classes (job types excluded) and the
    /// other public <c>*Stage</c> constants, found by reflection so a new stage is checked without touching this test.
    /// </summary>
    private static IEnumerable<string> StageKeys() =>
        typeof(ImportStages).Assembly.GetExportedTypes()
            .Where(type => type.IsAbstract && type.IsSealed)
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Where(field => !field.Name.EndsWith("Job", StringComparison.Ordinal))
            .Where(field => field.DeclaringType!.Name.EndsWith("Stages", StringComparison.Ordinal) || field.Name.EndsWith("Stage", StringComparison.Ordinal))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Distinct(StringComparer.Ordinal);

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
