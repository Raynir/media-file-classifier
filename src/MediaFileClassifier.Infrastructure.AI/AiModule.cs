using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Errors;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Jobs;
using MediaFileClassifier.Infrastructure.AI.Embeddings;
using MediaFileClassifier.Infrastructure.AI.Models;
using MediaFileClassifier.Infrastructure.AI.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace MediaFileClassifier.Infrastructure.AI;

/// <summary>
/// Registers the AI provider infrastructure (E01-S05).
/// </summary>
public static class AiModule
{
    /// <summary>
    /// Adds the provider registry, the model store and the provider-kind helpers. Concrete providers (E06) register
    /// themselves as <see cref="IAiProvider"/>, or are added to the registry at run time.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<OnnxSessionFactory>();
        services.TryAddSingleton<IExecutionProviderStatus, ExecutionProviderStatus>();
        services.TryAddSingleton<IAiProviderRegistry, AiProviderRegistry>();
        services.TryAddSingleton<IModelStore, ModelStore>();
        services.TryAddSingleton<IAnalysisImageDecoder, Imaging.SkiaAnalysisImageDecoder>();
        services.TryAddSingleton(new Siglip2Options());
        services.TryAddSingleton<Siglip2EmbeddingProvider>();
        services.AddSingleton<IAiProvider>(provider => provider.GetRequiredService<Siglip2EmbeddingProvider>());
        // The default OCR engine on every hardware class: registered before endpoint providers, so it's chosen first.
        services.TryAddSingleton(new Ocr.RapidOcrOptions());
        services.TryAddSingleton<Ocr.RapidOcrProvider>();
        services.AddSingleton<IAiProvider>(provider => provider.GetRequiredService<Ocr.RapidOcrProvider>());
        services.TryAddSingleton<IAiEndpointService, OpenAiCompatible.AiEndpointService>();
        services.TryAddSingleton<IModelCatalog, ModelCatalog>();
        services.TryAddSingleton<Benchmarks.BenchmarkResultStore>();
        services.TryAddSingleton<IBenchmarkResults>(provider => provider.GetRequiredService<Benchmarks.BenchmarkResultStore>());
        services.TryAddSingleton<IThroughputHints>(provider => provider.GetRequiredService<Benchmarks.BenchmarkResultStore>());
        services.TryAddSingleton<IBenchmarkRunner, Benchmarks.Siglip2Benchmark>();
        services.TryAddSingleton<IModelDownloader, ModelDownloader>();
        services.TryAddSingleton<IModelAvailability, ModelAvailability>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionClassifier, AiExceptionClassifier>());
        return services;
    }
}
