namespace MediaFileClassifier.Infrastructure.Abstractions.AI;

/// <summary>
/// Provider kind keys stored in <see cref="ModelIdentity.ProviderId"/> and in model registrations.
/// </summary>
public static class ProviderIds
{
    public const string Onnx = "onnx";

    public const string OpenAiCompatible = "openai-compatible";

    public const string NativeLibrary = "native";

    public const string Fake = "fake";
}
