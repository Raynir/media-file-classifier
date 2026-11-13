namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The execution provider the user chose on the hardware settings.
/// </summary>
public enum ExecutionProviderChoice
{
    /// <summary>Chosen from the hardware.</summary>
    Automatic,

    /// <summary>The CPU.</summary>
    Cpu,

    /// <summary>DirectML on the graphics adapter.</summary>
    DirectMl,

    /// <summary>CUDA on an NVIDIA GPU.</summary>
    Cuda,

    /// <summary>The Android Neural Networks API.</summary>
    Nnapi,
}

/// <summary>
/// The model tier the user chose on the hardware settings.
/// </summary>
public enum ModelTierChoice
{
    /// <summary>The installed tier that suits the execution provider.</summary>
    Automatic,

    /// <summary>The <c>standard</c> tier (fp32, for the CPU).</summary>
    Standard,

    /// <summary>The <c>compact</c> tier (fp16 text tower, for a GPU).</summary>
    Compact,
}

/// <summary>
/// The overrides of the hardware settings (E17-S08-T02). They're app settings, so each computer keeps its own. The
/// hardware section edits them itself, so they aren't registered as generated controls.
/// </summary>
public static class HardwareSettings
{
    /// <summary>
    /// The execution provider to use instead of the detected one.
    /// </summary>
    public static readonly ChoiceSetting<ExecutionProviderChoice> ExecutionProvider = new(
        "ai.executionProvider", SettingScope.App, SettingCategories.AiModels, "Hardware_Provider", ExecutionProviderChoice.Automatic);

    /// <summary>
    /// The model tier to use instead of the one that suits the execution provider.
    /// </summary>
    public static readonly ChoiceSetting<ModelTierChoice> ModelTier = new(
        "ai.modelTier", SettingScope.App, SettingCategories.AiModels, "Hardware_Tier", ModelTierChoice.Automatic);

    /// <summary>
    /// Gets the tier ID of a tier choice.
    /// </summary>
    /// <param name="choice">The choice.</param>
    /// <returns>The tier ID, e.g. <c>compact</c>, or <see langword="null"/> for <see cref="ModelTierChoice.Automatic"/>.</returns>
    public static string? TierIdOf(ModelTierChoice choice) => choice switch
    {
        ModelTierChoice.Standard => "standard",
        ModelTierChoice.Compact => "compact",
        _ => null,
    };
}
