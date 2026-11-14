using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;
using MediaFileClassifier.UseCases.Search.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests.Search.Filters;

/// <summary>
/// Tests for <see cref="FilterRegistry"/> (E10-S01-T01): a filter is added by registering a definition.
/// </summary>
public sealed class FilterRegistryTests
{
    [Fact]
    public void ARegisteredDefinition_IsInTheRegistry()
    {
        var extra = new TestFilter<BooleanValue>("extra", FilterValueKind.Boolean, 50);
        using var provider = new ServiceCollection()
            .AddUseCases()
            .AddSingleton<IFilterDefinition>(extra)
            .BuildServiceProvider();

        var registry = provider.GetRequiredService<FilterRegistry>();

        Assert.Same(extra, registry.Find("extra"));
        Assert.Contains(extra, registry.All);
    }

    [Fact]
    public void All_IsInPanelOrder_ThenById()
    {
        var late = new TestFilter<BooleanValue>("b-late", FilterValueKind.Boolean, 10);
        var tie = new TestFilter<BooleanValue>("a-tie", FilterValueKind.Boolean, 10);
        var early = new TestFilter<BooleanValue>("z-early", FilterValueKind.Boolean, 1);

        var registry = new FilterRegistry([late, tie, early]);

        Assert.Equal(["z-early", "a-tie", "b-late"], registry.All.Select(filter => filter.Id));
    }

    [Fact]
    public void Available_LeavesOutUnavailableFilters()
    {
        var hidden = new TestFilter<BooleanValue>("hidden", FilterValueKind.Boolean);
        var shown = new TestFilter<BooleanValue>("shown", FilterValueKind.Boolean);
        var registry = new FilterRegistry([hidden, shown]);
        var raised = 0;
        hidden.AvailabilityChanged += (_, _) => raised++;

        hidden.SetAvailable(false);

        Assert.Equal(1, raised);
        Assert.Equal([shown], registry.Available);
        Assert.Equal(2, registry.All.Count);
    }

    [Fact]
    public void Find_UnknownId_IsNull() => Assert.Null(new FilterRegistry(TestFilters.All).Find("nope"));

    [Fact]
    public void TwoFiltersWithOneId_Throw()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new FilterRegistry(
            [new TestFilter<BooleanValue>("same", FilterValueKind.Boolean), new TestFilter<RangeValue>("same", FilterValueKind.Range)]));

        Assert.Contains("same", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AValueTypeThatIsNotAFilterValue_Throws() =>
        Assert.Throws<InvalidOperationException>(() => new FilterRegistry([new WrongValueType()]));

    private sealed class WrongValueType : IFilterDefinition
    {
        public event EventHandler? AvailabilityChanged
        {
            add { }
            remove { }
        }

        public string Id => "wrong";

        public string DisplayNameKey => "Filter_Wrong";

        public int Order => 0;

        public FilterValueKind Kind => FilterValueKind.Boolean;

        public Type ValueType => typeof(string);

        public bool IsAvailable => true;

        public string? ToSql(FilterValue value, IFilterSql sql) => null;
    }
}
