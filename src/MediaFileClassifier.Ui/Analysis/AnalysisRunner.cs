using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Analysis.AnalyzeScope;
using MediatR;

namespace MediaFileClassifier.Ui.Analysis;

/// <summary>
/// Starts a bulk analysis from the UI (E06-S06-T02): finds the items of a scope, asks before a large run, and queues
/// the job. The count asked about is the count queued, because the job takes the items found here.
/// </summary>
/// <param name="sender">Sends the scope query and command.</param>
/// <param name="dialogs">The confirmation and notices.</param>
/// <param name="localizer">UI strings.</param>
public sealed class AnalysisRunner(ISender sender, IAnalysisDialogs dialogs, Localizer localizer)
{
    /// <summary>
    /// The number of items from which a run asks first.
    /// </summary>
    public const int ConfirmFrom = 500;

    /// <summary>
    /// Analyzes the items of a scope.
    /// </summary>
    /// <param name="scope">The scope.</param>
    /// <param name="analyzers">Analyzer keys to run, or <see langword="null"/> for the whole profile.</param>
    /// <param name="alreadyConfirmed">Whether the user already agreed, so no question is asked.</param>
    /// <returns>The result, or <see langword="null"/> when nothing was queued.</returns>
    public async Task<AnalyzeScopeResult?> StartAsync(AnalysisScope scope, IReadOnlyCollection<string>? analyzers = null, bool alreadyConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var items = await sender.Send(new ResolveAnalysisScopeQuery(scope));
        if (items.Count == 0)
        {
            await dialogs.ShowAsync(localizer.Get("Analyze_NothingToAnalyze"));
            return null;
        }

        if (!alreadyConfirmed && items.Count >= ConfirmFrom
            && !await dialogs.ConfirmAsync(
                localizer.Format("Analyze_Confirm", IndexedFolderViewModel.ItemCountText(localizer, items.Count)),
                localizer.Get("Analyze_Start"),
                localizer.Get("Analyze_Cancel")))
        {
            return null;
        }

        var description = AnalyzeScopeCommandHandler.Describe(scope, items.Count);
        var result = await sender.Send(new AnalyzeScopeCommand(AnalysisScope.Items(items.ItemIds), analyzers, description));
        if (result.JobId is null)
        {
            await dialogs.ShowAsync(localizer.Get("Analyze_NotRunning"));
            return null;
        }

        return result;
    }
}
