using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.SearchResults;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The search box in the toolbar's Search slot (E09-S01-T05). Typing starts a search once the keystrokes pause for
/// <see cref="DefaultDelay"/> (D031); the search before it is cancelled. Enter searches at once, and Clear goes
/// back to browsing.
/// </summary>
public sealed partial class SearchBarViewModel : ViewModelBase
{
    /// <summary>
    /// How long the text must rest before it is searched (D031).
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly SearchResultSource source;
    private readonly DispatcherTimer timer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to search.</param>
    /// <param name="delay">The keystroke pause, or <see langword="null"/> for <see cref="DefaultDelay"/>.</param>
    public SearchBarViewModel(SearchResultSource source, TimeSpan? delay = null)
    {
        this.source = source;
        timer = new DispatcherTimer { Interval = delay ?? DefaultDelay };
        timer.Tick += (_, _) => Submit();
    }

    /// <summary>
    /// Gets or sets what the user typed.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>
    /// Searches for the text now, without waiting for the pause.
    /// </summary>
    [RelayCommand]
    public void Submit()
    {
        timer.Stop();
        source.SetText(Text);
    }

    /// <summary>
    /// Empties the box and goes back to browsing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    public void Clear()
    {
        Text = string.Empty;
        Submit();
    }

    partial void OnTextChanged(string value)
    {
        timer.Stop();
        timer.Start();
    }

    private bool CanClear() => Text.Length > 0;
}
