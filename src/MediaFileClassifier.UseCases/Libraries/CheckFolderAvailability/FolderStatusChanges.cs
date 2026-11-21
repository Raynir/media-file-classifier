namespace MediaFileClassifier.UseCases.Libraries.CheckFolderAvailability;

/// <summary>
/// Reports that an indexed folder went Offline or came back Online (E14-S07-T04), so what shows its items' availability
/// refreshes: the results, the details panel through them, and the offline folders notice. A folder's status isn't
/// part of its items' rows, so <c>IMediaItemRepository.Changed</c> doesn't cover it.
/// </summary>
public sealed class FolderStatusChanges
{
    /// <summary>
    /// Raised after an availability check changed the status of one or more folders. It's raised on the checking
    /// thread, once per check.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Raises <see cref="Changed"/>.
    /// </summary>
    public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
