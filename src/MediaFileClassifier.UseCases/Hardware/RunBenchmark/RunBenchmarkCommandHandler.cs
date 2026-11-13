using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediatR;

namespace MediaFileClassifier.UseCases.Hardware.RunBenchmark;

/// <summary>
/// Handles <see cref="RunBenchmarkCommand"/>.
/// </summary>
/// <param name="runner">The benchmark.</param>
public sealed class RunBenchmarkCommandHandler(IBenchmarkRunner runner) : IRequestHandler<RunBenchmarkCommand, BenchmarkReport>
{
    /// <inheritdoc />
    public Task<BenchmarkReport> Handle(RunBenchmarkCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return runner.RunAsync(request.Progress, cancellationToken);
    }
}
