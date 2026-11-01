using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.UseCases.Tests;

/// <summary>
/// Tests for <see cref="UseCasesModule"/>.
/// </summary>
public sealed class UseCasesModuleTests
{
    [Fact]
    public void AddUseCases_RegistersMediator()
    {
        using var provider = new ServiceCollection().AddUseCases().BuildServiceProvider();

        Assert.NotNull(provider.GetService<IMediator>());
    }
}
