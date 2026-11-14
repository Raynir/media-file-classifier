namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The thumbnail size slider (E14-S01-T05) in the toolbar's View slot (E13-S03). It sets the grid's tile size,
/// as <c>=</c>, <c>-</c> and Ctrl+mouse wheel do (D046).
/// </summary>
/// <param name="grid">Gets the thumbnail grid. It's resolved the first time the slider shows, once a library is open.</param>
public sealed class ThumbnailSizeViewModel(Func<ThumbnailGridViewModel> grid) : ViewModelBase
{
    private ThumbnailGridViewModel? shown;

    /// <summary>
    /// Gets the thumbnail grid whose tile size the slider sets.
    /// </summary>
    public ThumbnailGridViewModel Grid => shown ??= grid();
}
