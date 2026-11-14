using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// One of the quick filter chips in the results header (All / Images / Videos / Favorites, E10-S02-T01).
/// </summary>
public sealed partial class QuickFilterViewModel : ViewModelBase
{
    private readonly Action apply;

    /// <summary>
    /// Initializes a new instance of the <see cref="QuickFilterViewModel"/> class.
    /// </summary>
    /// <param name="text">The text shown.</param>
    /// <param name="apply">Applies the filter.</param>
    public QuickFilterViewModel(string text, Action apply)
    {
        Text = text;
        this.apply = apply;
    }

    /// <summary>
    /// Gets the text shown.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets a value indicating whether the filter is the one in force.
    /// </summary>
    [ObservableProperty]
    public partial bool IsChecked { get; internal set; }

    /// <summary>
    /// Applies the filter.
    /// </summary>
    [RelayCommand]
    public void Apply()
    {
        apply();

        // A toggle button flips itself when clicked; telling it the state again, after the search has updated it,
        // puts it right when the click changed nothing (clicking the chip already in force).
        Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(IsChecked)));
    }
}
