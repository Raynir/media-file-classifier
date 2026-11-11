using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.UseCases.Search.LoadReferenceImage;
using MediaFileClassifier.UseCases.Search.SearchResults;
using MediatR;

namespace MediaFileClassifier.Ui.Search;

/// <summary>
/// The search box in the toolbar's Search slot (E09-S01-T05). Typing starts a search once the keystrokes pause for
/// <see cref="DefaultDelay"/> (D031); the search before it is cancelled. Enter searches at once, and Clear goes
/// back to browsing. A picture picked with the image button, dropped on the box or pasted into it starts an
/// image-to-image search instead and shows as a chip (E09-S04-T01); typing replaces it.
/// </summary>
public sealed partial class SearchBarViewModel : ViewModelBase
{
    /// <summary>
    /// How long the text must rest before it is searched (D031).
    /// </summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(300);

    private readonly SearchResultSource source;
    private readonly ISender sender;
    private readonly IReferenceImagePicker picker;
    private readonly Localizer localizer;
    private readonly DispatcherTimer timer;
    private bool settingImage;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchBarViewModel"/> class.
    /// </summary>
    /// <param name="source">The results to search.</param>
    /// <param name="sender">Checks the reference pictures.</param>
    /// <param name="picker">Asks for a reference picture.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="delay">The keystroke pause, or <see langword="null"/> for <see cref="DefaultDelay"/>.</param>
    public SearchBarViewModel(SearchResultSource source, ISender sender, IReferenceImagePicker picker, Localizer localizer, TimeSpan? delay = null)
    {
        this.source = source;
        this.sender = sender;
        this.picker = picker;
        this.localizer = localizer;
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
    /// Gets the picture being searched with, shown as the chip, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReferenceImage))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial ReferenceImage? ReferenceImage { get; private set; }

    /// <summary>
    /// Gets why the last picture couldn't be used, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasImageError))]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    public partial string? ImageError { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the chip shows.
    /// </summary>
    public bool HasReferenceImage => ReferenceImage is not null;

    /// <summary>
    /// Gets a value indicating whether the picture error shows.
    /// </summary>
    public bool HasImageError => ImageError is not null;

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
    /// Empties the box, removes the picture and goes back to browsing.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanClear))]
    public void Clear()
    {
        ImageError = null;
        if (ReferenceImage is not null)
        {
            RemoveImage();
        }

        Text = string.Empty;
        Submit();
    }

    /// <summary>
    /// Removes the picture and goes back to browsing.
    /// </summary>
    [RelayCommand]
    public void RemoveImage()
    {
        ReferenceImage = null;
        source.SetReferenceImage(null);
    }

    /// <summary>
    /// Asks for a picture and searches with it.
    /// </summary>
    /// <returns>A task that completes when the search has started, or the picture was refused.</returns>
    [RelayCommand]
    public async Task PickImageAsync()
    {
        PickedImage? picked;
        try
        {
            picked = await picker.PickAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ImageError = localizer.Get("Search_ImageError_Unreadable");
            return;
        }

        if (picked is not null)
        {
            await UseImageAsync(picked);
        }
    }

    /// <summary>
    /// Searches with a picture that was picked, dropped or pasted, once it's checked. A picture that isn't one of the
    /// supported images shows an error and leaves the search as it is.
    /// </summary>
    /// <param name="picture">The picture.</param>
    /// <returns>A task that completes when the search has started, or the picture was refused.</returns>
    public async Task UseImageAsync(PickedImage picture)
    {
        ArgumentNullException.ThrowIfNull(picture);

        var load = await sender.Send(new LoadReferenceImageQuery(picture.Content, picture.Name));
        if (load.Image is not { } image)
        {
            ImageError = localizer.Get(load.Problem switch
            {
                ReferenceImageProblem.TooLarge => "Search_ImageError_TooLarge",
                ReferenceImageProblem.Unreadable => "Search_ImageError_Unreadable",
                _ => "Search_ImageError_Unsupported",
            });
            return;
        }

        // The picture replaces the text: emptying the box must not start a text search of its own.
        timer.Stop();
        settingImage = true;
        try
        {
            Text = string.Empty;
        }
        finally
        {
            settingImage = false;
        }

        ImageError = null;
        ReferenceImage = image;
        source.SetReferenceImage(image);
    }

    /// <summary>
    /// Shows that a picture couldn't be read, for example from a drop.
    /// </summary>
    public void ShowUnreadableImage() => ImageError = localizer.Get("Search_ImageError_Unreadable");

    partial void OnTextChanged(string value)
    {
        if (settingImage)
        {
            return;
        }

        ImageError = null;

        // Typing replaces the picture at once, so the chip and the results agree; the text search follows after the pause.
        if (value.Length > 0 && ReferenceImage is not null)
        {
            RemoveImage();
        }

        timer.Stop();
        timer.Start();
    }

    private bool CanClear() => Text.Length > 0 || ReferenceImage is not null || ImageError is not null;
}
