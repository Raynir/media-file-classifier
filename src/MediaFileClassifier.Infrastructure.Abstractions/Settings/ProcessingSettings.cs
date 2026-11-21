namespace MediaFileClassifier.Infrastructure.Abstractions.Settings;

/// <summary>
/// The processing settings (E17-S03, E05-S01): the concurrency limit of each resource pool, what happens to
/// unfinished work when a library opens, and whether quitting while work is pending asks first. They're app settings,
/// so each computer keeps its own. The CPU default comes from the hardware (E01-S07-T04), so the definitions are made
/// once at startup from the recommended value.
/// </summary>
public sealed class ProcessingSettings
{
    /// <summary>
    /// The default concurrency limit of the I/O pool.
    /// </summary>
    public const int DefaultIoConcurrency = 4;

    /// <summary>
    /// The default concurrency limit of the AI pool.
    /// </summary>
    public const int DefaultAiConcurrency = 1;

    /// <summary>
    /// The largest concurrency limit of the I/O pool.
    /// </summary>
    public const int MaxIoConcurrency = 16;

    /// <summary>
    /// The largest concurrency limit of the CPU pool.
    /// </summary>
    public const int MaxCpuConcurrency = 32;

    /// <summary>
    /// The largest concurrency limit of the AI pool.
    /// </summary>
    public const int MaxAiConcurrency = 4;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessingSettings"/> class.
    /// </summary>
    /// <param name="recommendedCpuConcurrency">The CPU pool's default limit for this computer. Clamped to the allowed range.</param>
    public ProcessingSettings(int recommendedCpuConcurrency)
    {
        IoConcurrency = new IntegerSetting(
            "processing.ioConcurrency", SettingScope.App, SettingCategories.Processing, "Setting_IoConcurrency", DefaultIoConcurrency, 1, MaxIoConcurrency)
        {
            HelpKey = "Setting_IoConcurrency_Help",
            Order = 100,
        };
        CpuConcurrency = new IntegerSetting(
            "processing.cpuConcurrency",
            SettingScope.App,
            SettingCategories.Processing,
            "Setting_CpuConcurrency",
            Math.Clamp(recommendedCpuConcurrency, 1, MaxCpuConcurrency),
            1,
            MaxCpuConcurrency)
        {
            HelpKey = "Setting_CpuConcurrency_Help",
            Order = 110,
        };
        AiConcurrency = new IntegerSetting(
            "processing.aiConcurrency", SettingScope.App, SettingCategories.Processing, "Setting_AiConcurrency", DefaultAiConcurrency, 1, MaxAiConcurrency)
        {
            HelpKey = "Setting_AiConcurrency_Help",
            Order = 120,
        };
        ResumeOnOpen = new ChoiceSetting<ResumeMode>(
            "processing.resumeOnOpen", SettingScope.App, SettingCategories.Processing, "Setting_ResumeOnOpen", ResumeMode.Automatic)
        {
            HelpKey = "Setting_ResumeOnOpen_Help",
            Order = 200,
        };
        ConfirmQuitWhileProcessing = new BooleanSetting(
            "processing.confirmQuitWhileProcessing", SettingScope.App, SettingCategories.Processing, "Setting_ConfirmQuit", defaultValue: true)
        {
            HelpKey = "Setting_ConfirmQuit_Help",
            Order = 210,
        };
    }

    /// <summary>
    /// Gets the I/O pool's concurrency limit.
    /// </summary>
    public IntegerSetting IoConcurrency { get; }

    /// <summary>
    /// Gets the CPU pool's concurrency limit.
    /// </summary>
    public IntegerSetting CpuConcurrency { get; }

    /// <summary>
    /// Gets the AI pool's concurrency limit.
    /// </summary>
    public IntegerSetting AiConcurrency { get; }

    /// <summary>
    /// Gets what happens to unfinished work when a library opens. It's read when a library opens.
    /// </summary>
    public ChoiceSetting<ResumeMode> ResumeOnOpen { get; }

    /// <summary>
    /// Gets whether closing the app while work is pending asks first (D027). "Don't ask again" turns it off.
    /// </summary>
    public BooleanSetting ConfirmQuitWhileProcessing { get; }

    /// <summary>
    /// Gets every setting, for registering them.
    /// </summary>
    public IReadOnlyList<SettingDefinition> All => [IoConcurrency, CpuConcurrency, AiConcurrency, ResumeOnOpen, ConfirmQuitWhileProcessing];
}
