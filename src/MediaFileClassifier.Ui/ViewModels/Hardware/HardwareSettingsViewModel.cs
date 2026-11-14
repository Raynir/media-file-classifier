using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Hardware;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Hardware.GetHardwareStatus;
using MediaFileClassifier.UseCases.Hardware.RunBenchmark;
using MediaFileClassifier.UseCases.Settings.SetSettingValue;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.ViewModels.Hardware;

/// <summary>
/// An option of a hardware drop-down: a value, its label, and why it can't be chosen here.
/// </summary>
/// <param name="Value">An <see cref="ExecutionProviderChoice"/> or <see cref="ModelTierChoice"/>.</param>
/// <param name="Label">The option's text.</param>
/// <param name="Reason">Why it can't be chosen, or <see langword="null"/> when it can.</param>
public sealed record HardwareOption(object Value, string Label, string? Reason)
{
    /// <summary>
    /// Gets a value indicating whether the option can be chosen.
    /// </summary>
    public bool IsEnabled => Reason is null;
}

/// <summary>
/// One line of the benchmark results.
/// </summary>
/// <param name="Analyzer">What was timed, e.g. "Images".</param>
/// <param name="Tier">The model tier.</param>
/// <param name="Provider">Where it ran, e.g. "DirectML".</param>
/// <param name="Speed">The speed, e.g. "12.3 per second".</param>
public sealed record BenchmarkRow(string Analyzer, string Tier, string Provider, string Speed);

/// <summary>
/// The hardware section of the AI &amp; Models settings (E17-S08-T02): the detected execution provider, overrides of the
/// provider and the model tier (options that can't be used here are disabled, with the reason, E01-S07-T03), and the
/// benchmark. Overrides are app settings, and the AI provider applies them from the next analysis.
/// </summary>
public sealed partial class HardwareSettingsViewModel : ViewModelBase, IDisposable
{
    private readonly ISender sender;
    private readonly ISettingsStore store;
    private readonly Localizer localizer;
    private readonly ILogger logger;
    private HardwareStatus? status;
    private CancellationTokenSource? benchmark;
    private bool updating;
    private bool loadFailed;
    private int refreshes;
    private readonly IModelAvailability? availability;

    /// <summary>
    /// Initializes a new instance of the <see cref="HardwareSettingsViewModel"/> class.
    /// </summary>
    /// <param name="sender">Sends use case requests.</param>
    /// <param name="store">The settings store, for the current overrides.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="availability">Tells when models are installed or deleted, or <see langword="null"/> where the app runs without AI.</param>
    /// <param name="logger">Logger.</param>
    public HardwareSettingsViewModel(
        ISender sender,
        ISettingsStore store,
        Localizer localizer,
        IModelAvailability? availability = null,
        ILogger<HardwareSettingsViewModel>? logger = null)
    {
        this.sender = sender;
        this.store = store;
        this.localizer = localizer;
        this.logger = logger ?? (ILogger)NullLogger.Instance;
        store.Changed += OnSettingChanged;
        localizer.Changed += OnLocalizerChanged;
        this.availability = availability;
        if (availability is not null)
        {
            // Installing or deleting a tier changes which tiers can be chosen.
            availability.Changed += OnModelsChanged;
            Dispatcher.UIThread.Post(() => _ = RefreshAsync());
        }
    }

    /// <summary>
    /// Gets the detected execution provider, e.g. "DirectML on NVIDIA GeForce RTX 4070".
    /// </summary>
    [ObservableProperty]
    public partial string Detected { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the execution provider options.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<HardwareOption> Providers { get; private set; } = [];

    /// <summary>
    /// Gets or sets the chosen execution provider option.
    /// </summary>
    [ObservableProperty]
    public partial HardwareOption? SelectedProvider { get; set; }

    /// <summary>
    /// Gets the model tier options.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<HardwareOption> Tiers { get; private set; } = [];

    /// <summary>
    /// Gets or sets the chosen model tier option.
    /// </summary>
    [ObservableProperty]
    public partial HardwareOption? SelectedTier { get; set; }

    /// <summary>
    /// Gets why some options can't be chosen, one line per option, so the reasons are readable without opening the lists.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Unavailable { get; private set; } = [];

    /// <summary>
    /// Gets when the benchmark last ran, or that it never ran.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkSummary { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the latest benchmark's results.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<BenchmarkRow> BenchmarkRows { get; private set; } = [];

    /// <summary>
    /// Gets the step the running benchmark is on.
    /// </summary>
    [ObservableProperty]
    public partial string BenchmarkProgress { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the benchmark is running.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsBenchmarking { get; private set; }

    /// <summary>
    /// Gets what went wrong, or an empty string.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string Error { get; private set; } = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the benchmark can be started.
    /// </summary>
    public bool IsIdle => !IsBenchmarking;

    /// <summary>
    /// Gets a value indicating whether <see cref="Error"/> shows.
    /// </summary>
    public bool HasError => Error.Length > 0;

    /// <summary>
    /// Reads the hardware, the installed tiers and the latest benchmark again.
    /// </summary>
    /// <returns>A task that completes when the section is up to date.</returns>
    public async Task RefreshAsync()
    {
        var refresh = ++refreshes;
        HardwareStatus latest;
        try
        {
            latest = await sender.Send(new GetHardwareStatusQuery());
        }
        catch (Exception ex) when (ex is ModelStoreException or IOException or UnauthorizedAccessException)
        {
            LogRefreshFailed(ex);
            Error = localizer.Get("Hardware_LoadFailed");
            loadFailed = true;
            return;
        }

        if (refresh != refreshes)
        {
            // A later refresh started meanwhile; its result is newer.
            return;
        }

        status = latest;
        if (loadFailed)
        {
            // Only the refresh's own error: a benchmark failure stays until the next action.
            Error = string.Empty;
            loadFailed = false;
        }

        Show();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        store.Changed -= OnSettingChanged;
        if (availability is not null)
        {
            availability.Changed -= OnModelsChanged;
        }

        localizer.Changed -= OnLocalizerChanged;
        benchmark?.Cancel();
        benchmark?.Dispose();
    }

    [RelayCommand]
    private async Task RunBenchmarkAsync()
    {
        using var cancellation = new CancellationTokenSource();
        benchmark = cancellation;
        IsBenchmarking = true;
        Error = string.Empty;
        BenchmarkProgress = localizer.Get("Hardware_BenchmarkStarting");
        try
        {
            await sender.Send(new RunBenchmarkCommand(new Progress<string>(line => BenchmarkProgress = line)), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // The previous report stays.
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The benchmark loads models and runs them; anything it throws leaves the old report and is shown.
            LogBenchmarkFailed(ex);
            Error = localizer.Get("Hardware_BenchmarkFailed");
        }
        finally
        {
            benchmark = null;
            IsBenchmarking = false;
            BenchmarkProgress = string.Empty;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void CancelBenchmark() => benchmark?.Cancel();

    partial void OnSelectedProviderChanged(HardwareOption? value)
    {
        if (!updating && value is { IsEnabled: true, Value: ExecutionProviderChoice choice })
        {
            _ = SaveAsync(HardwareSettings.ExecutionProvider, choice);
        }
    }

    partial void OnSelectedTierChanged(HardwareOption? value)
    {
        if (!updating && value is { IsEnabled: true, Value: ModelTierChoice choice })
        {
            _ = SaveAsync(HardwareSettings.ModelTier, choice);
        }
    }

    private async Task SaveAsync(SettingDefinition setting, object value)
    {
        var result = await sender.Send(new SetSettingValueCommand(setting, value));
        Error = result.Succeeded ? string.Empty : localizer.Get(result.Error!.MessageKey);
    }

    private void Show()
    {
        if (status is null)
        {
            return;
        }

        var gpu = status.GpuName;
        var detected = ProviderName(status.Detected);
        Detected = status.Detected == ExecutionProvider.Cpu || gpu is null ? detected : localizer.Format("Hardware_DetectedOn", detected, gpu);

        var providers = new List<HardwareOption> { new(ExecutionProviderChoice.Automatic, localizer.Format("Hardware_Automatic", detected), null) };
        var unavailable = new List<string>();
        foreach (var provider in status.Providers)
        {
            var name = ProviderName(provider.Provider);
            var reason = provider.UnavailableReason switch
            {
                ExecutionProviderUnavailableReason.NotInRuntime => localizer.Format("Hardware_Reason_NotInRuntime", name),
                ExecutionProviderUnavailableReason.NoSuitableGpu when provider.Provider == ExecutionProvider.Cuda => localizer.Format("Hardware_Reason_NoNvidiaGpu", name),
                ExecutionProviderUnavailableReason.NoSuitableGpu => localizer.Format("Hardware_Reason_NoSuitableGpu", name),
                _ => null,
            };
            var label = provider.Provider != ExecutionProvider.Cpu && reason is null && gpu is not null
                ? localizer.Format("Hardware_DetectedOn", name, gpu)
                : name;
            providers.Add(new HardwareOption(ChoiceOf(provider.Provider), label, reason));
            if (reason is not null)
            {
                unavailable.Add(reason);
            }
        }

        var tiers = new List<HardwareOption> { new(ModelTierChoice.Automatic, localizer.Get("Hardware_TierAutomatic"), null) };
        foreach (var choice in new[] { ModelTierChoice.Standard, ModelTierChoice.Compact })
        {
            var name = localizer.Get("Hardware_Tier_" + choice);
            var reason = status.InstalledTiers.Contains(HardwareSettings.TierIdOf(choice)) ? null : localizer.Format("Hardware_Reason_TierNotInstalled", name);
            tiers.Add(new HardwareOption(choice, name, reason));
            if (reason is not null)
            {
                unavailable.Add(reason);
            }
        }

        updating = true;
        try
        {
            Providers = providers;
            Tiers = tiers;
            Unavailable = unavailable;
            SelectStored();
        }
        finally
        {
            updating = false;
        }

        ShowBenchmark(status.Benchmark);
    }

    private void SelectStored()
    {
        var provider = store.Get(HardwareSettings.ExecutionProvider);
        var tier = store.Get(HardwareSettings.ModelTier);

        // A stored choice that isn't listed here (e.g. NNAPI from another platform) shows as Automatic.
        SelectedProvider = Providers.FirstOrDefault(option => option.Value.Equals(provider)) ?? Providers.FirstOrDefault();
        SelectedTier = Tiers.FirstOrDefault(option => option.Value.Equals(tier)) ?? Tiers.FirstOrDefault();
    }

    private void ShowBenchmark(BenchmarkReport? report)
    {
        if (report is null)
        {
            BenchmarkSummary = localizer.Get("Hardware_BenchmarkNever");
            BenchmarkRows = [];
            return;
        }

        BenchmarkSummary = report.Entries.Count == 0
            ? localizer.Get("Hardware_BenchmarkNoModel")
            : localizer.Format("Hardware_BenchmarkMeasured", report.MeasuredAt.ToLocalTime().ToString("g", localizer.Culture ?? CultureInfo.CurrentCulture));
        BenchmarkRows = [.. report.Entries.Select(entry => new BenchmarkRow(
            localizer.Find("Hardware_Analyzer_" + entry.Analyzer.Replace('.', '_')) ?? entry.Analyzer,
            localizer.Find("Hardware_Tier_" + CultureInfo.InvariantCulture.TextInfo.ToTitleCase(entry.TierId)) ?? entry.TierId,
            ProviderName(entry.Provider),
            localizer.Format("Hardware_Speed", entry.ItemsPerSecond.ToString("0.0", localizer.Culture ?? CultureInfo.CurrentCulture))))];
    }

    private string ProviderName(ExecutionProvider provider) => localizer.Get("Hardware_Provider_" + provider);

    private static ExecutionProviderChoice ChoiceOf(ExecutionProvider provider) => provider switch
    {
        ExecutionProvider.DirectMl => ExecutionProviderChoice.DirectMl,
        ExecutionProvider.Cuda => ExecutionProviderChoice.Cuda,
        ExecutionProvider.Nnapi => ExecutionProviderChoice.Nnapi,
        _ => ExecutionProviderChoice.Cpu,
    };

    private void OnSettingChanged(object? sender, SettingChangedEventArgs e)
    {
        if (e.Setting != HardwareSettings.ExecutionProvider && e.Setting != HardwareSettings.ModelTier)
        {
            return;
        }

        void Select()
        {
            updating = true;
            try
            {
                SelectStored();
            }
            finally
            {
                updating = false;
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            Select();
        }
        else
        {
            Dispatcher.UIThread.Post(Select);
        }
    }

    private void OnLocalizerChanged(object? sender, EventArgs e) => Show();

    private void OnModelsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() => _ = RefreshAsync());

    [LoggerMessage(Level = LogLevel.Warning, Message = "Reading the hardware status failed")]
    private partial void LogRefreshFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The benchmark failed")]
    private partial void LogBenchmarkFailed(Exception exception);
}
