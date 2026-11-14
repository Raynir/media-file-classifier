using Avalonia.Threading;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Repositories;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Ui.Analysis;

/// <summary>
/// The analysis commands (E06-S06): Item › Analyze and Analyze only with… for the selection, Library › Analyze pending
/// items and Analyze all results. It also offers to analyze the pending items when the user switches "analyze
/// automatically" on, since items imported while it was off stayed Pending.
/// </summary>
/// <param name="sender">Sends queries.</param>
/// <param name="runner">Starts the analysis.</param>
/// <param name="selection">The selected items.</param>
/// <param name="dialogs">The dialogs.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="settings">The setting values, to watch the automatic analysis setting.</param>
/// <param name="session">The open library.</param>
/// <param name="logger">Logger.</param>
public sealed partial class AnalysisCommands(
    ISender sender,
    AnalysisRunner runner,
    SelectionModel selection,
    IAnalysisDialogs dialogs,
    Localizer localizer,
    ISettingsStore? settings = null,
    ILibrarySession? session = null,
    ILogger<AnalysisCommands>? logger = null) : ICommandContributor
{
    /// <summary>
    /// Item › Analyze.
    /// </summary>
    public const string Analyze = "item.analyze";

    /// <summary>
    /// Item › Analyze only with… › Search embedding.
    /// </summary>
    public const string AnalyzeEmbedding = "item.analyze-embedding";

    /// <summary>
    /// Item › Analyze only with… › Description and tags.
    /// </summary>
    public const string AnalyzeDescriber = "item.analyze-describer";

    /// <summary>
    /// Item › Analyze only with… › Text in image.
    /// </summary>
    public const string AnalyzeOcr = "item.analyze-ocr";

    /// <summary>
    /// Library › Analyze pending items.
    /// </summary>
    public const string AnalyzePending = "library.analyze-pending";

    /// <summary>
    /// Library › Analyze all results.
    /// </summary>
    public const string AnalyzeResults = "library.analyze-results";

    private const string OnlySubmenu = "Menu_AnalyzeOnly";

    private readonly ILogger log = logger ?? NullLogger<AnalysisCommands>.Instance;

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Add(new AppCommand(Analyze, CommandGroups.Item, 780, "Command_Analyze", () => AnalyzeSelectionAsync(null))
        {
            AppliesTo = HasSelection,
        });
        AddOnly(registry, AnalyzeEmbedding, 781, "Command_AnalyzeEmbedding", AnalyzerKeys.Embedding);
        AddOnly(registry, AnalyzeDescriber, 782, "Command_AnalyzeDescriber", AnalyzerKeys.Describer);
        AddOnly(registry, AnalyzeOcr, 783, "Command_AnalyzeOcr", AnalyzerKeys.Ocr);

        var pending = registry.Add(new AppCommand(AnalyzePending, CommandGroups.Library, 136, "Command_AnalyzePending", () => runner.StartAsync(AnalysisScope.Pending))
        {
            WhyDisabled = NoLibrary,
        });
        var results = registry.Add(new AppCommand(AnalyzeResults, CommandGroups.Library, 137, "Command_AnalyzeResults", () => runner.StartAsync(AnalysisScope.SearchResults))
        {
            WhyDisabled = NoLibrary,
        });

        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                pending.Refresh();
                results.Refresh();
            });
        }

        if (settings is not null)
        {
            settings.Changed += OnSettingChanged;
        }
    }

    /// <summary>
    /// Offers to analyze the pending items, as when the user switched automatic analysis on.
    /// </summary>
    /// <returns>A task that completes when the user answered and the analysis, if any, is queued.</returns>
    public async Task OfferPendingAsync()
    {
        try
        {
            var pending = await sender.Send(new CountLibraryItemsQuery(new MediaItemFilter { Status = Domain.Media.MediaItemStatus.Pending }));
            if (pending == 0)
            {
                return;
            }

            var items = IndexedFolderViewModel.ItemCountText(localizer, pending);
            if (await dialogs.ConfirmAsync(localizer.Format("Analyze_BacklogConfirm", items), localizer.Get("Analyze_BacklogNow"), localizer.Get("Analyze_BacklogLater")))
            {
                await runner.StartAsync(AnalysisScope.Pending, alreadyConfirmed: true);
            }
        }
        catch (InvalidOperationException exception)
        {
            // The library closed meanwhile.
            LogOfferFailed(exception);
        }
    }

    private bool HasSelection() => selection.Count > 0;

    private string? NoLibrary() => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null;

    private void AddOnly(CommandRegistry registry, string id, int order, string labelKey, string analyzer) =>
        registry.Add(new AppCommand(id, CommandGroups.Item, order, labelKey, () => AnalyzeSelectionAsync([analyzer]))
        {
            Submenu = OnlySubmenu,
            AppliesTo = HasSelection,
        });

    private async Task AnalyzeSelectionAsync(IReadOnlyCollection<string>? analyzers)
    {
        var ids = await sender.Send(new ResolveItemSelectionQuery(selection.Snapshot()));
        if (ids.Count > 0)
        {
            await runner.StartAsync(AnalysisScope.Items(ids), analyzers);
        }
    }

    private void OnSettingChanged(object? source, SettingChangedEventArgs e)
    {
        if (e.Setting.Key == KnownSettings.AnalyzeAutomatically.Key && settings!.Get(KnownSettings.AnalyzeAutomatically))
        {
            Dispatcher.UIThread.Post(() => _ = OfferPendingAsync());
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The offer to analyze the pending items couldn't be made")]
    private partial void LogOfferFailed(Exception exception);
}
