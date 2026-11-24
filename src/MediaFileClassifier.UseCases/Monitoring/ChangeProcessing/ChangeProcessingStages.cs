namespace MediaFileClassifier.UseCases.Monitoring.ChangeProcessing;

/// <summary>
/// The stage keys of change processing (E04-S02). They're stored with each work item, so they never change. The work
/// runs in an import job (<see cref="Import.ImportStages.ImportJob"/>) of the folder, so new files go on through the
/// import's stages and changed ones through the rescan's (<see cref="Rescan.RescanStages"/>) in the same job.
/// </summary>
public static class ChangeProcessingStages
{
    /// <summary>
    /// Applies a batch of a watched folder's changes to the library. The payload is a <see cref="ChangePayload"/>.
    /// </summary>
    public const string Apply = "monitor.apply";
}
