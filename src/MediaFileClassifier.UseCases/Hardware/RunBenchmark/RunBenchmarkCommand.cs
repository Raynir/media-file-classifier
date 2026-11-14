using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediatR;

namespace MediaFileClassifier.UseCases.Hardware.RunBenchmark;

/// <summary>
/// Runs the benchmark (E06-S08-T04) from the hardware settings and stores its report, which the job estimates use.
/// </summary>
/// <param name="Progress">Receives one line per step, or <see langword="null"/>.</param>
public sealed record RunBenchmarkCommand(IProgress<string>? Progress) : IRequest<BenchmarkReport>;
