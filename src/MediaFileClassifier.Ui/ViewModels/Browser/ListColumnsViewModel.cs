using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Ui.Browser;

namespace MediaFileClassifier.Ui.ViewModels.Browser;

/// <summary>
/// The list view's columns (E14-S02), in display order. The header and every row lay out their cells from it and
/// update when <see cref="LayoutChanged"/> fires. Columns can be shown, hidden, resized and moved (the thumbnail stays
/// first), and the setup is kept in an app setting, so it survives a restart (E14-S02-T02). Use it on the UI thread.
/// </summary>
public sealed partial class ListColumnsViewModel : ViewModelBase
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// The default setup: every column but the folder shows (MFC-453).
    /// </summary>
    private static readonly (ListColumnKind Kind, double Width, bool IsVisible)[] Defaults =
    [
        (ListColumnKind.Thumbnail, 48, true),
        (ListColumnKind.Name, 280, true),
        (ListColumnKind.Type, 72, true),
        (ListColumnKind.DateTaken, 150, true),
        (ListColumnKind.Size, 88, true),
        (ListColumnKind.Resolution, 110, true),
        (ListColumnKind.Duration, 80, true),
        (ListColumnKind.Status, 120, true),
        (ListColumnKind.Folder, 260, false),
        (ListColumnKind.Favorite, 40, true),
    ];

    private readonly ISettingsStore? settings;
    private readonly DispatcherTimer saveTimer;
    private bool applying;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListColumnsViewModel"/> class.
    /// </summary>
    /// <param name="settings">Keeps the setup, or <see langword="null"/> for the default setup, not kept.</param>
    public ListColumnsViewModel(ISettingsStore? settings = null)
    {
        this.settings = settings;
        saveTimer = new DispatcherTimer { Interval = SaveDelay };
        saveTimer.Tick += (_, _) => SaveNow();
        Apply(settings is null ? DefaultSetup() : Parse(settings.Get(BrowserSettings.ListColumns)));
    }

    /// <summary>
    /// Raised when a column's width, visibility or position changed, so the header and rows lay out again.
    /// </summary>
    public event EventHandler? LayoutChanged;

    /// <summary>
    /// Gets the columns in display order, hidden ones included.
    /// </summary>
    public ObservableCollection<ListColumnViewModel> Columns { get; } = [];

    /// <summary>
    /// Gets the width of the visible columns together.
    /// </summary>
    public double TotalWidth => Columns.Where(column => column.IsVisible).Sum(column => column.Width);

    /// <summary>
    /// Gets a column.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <returns>The column.</returns>
    public ListColumnViewModel this[ListColumnKind kind] => Columns.First(column => column.Kind == kind);

    /// <summary>
    /// Writes a setup as the setting stores it: the configurable columns in order, as <c>Kind:width</c>, with a
    /// <c>!</c> before a hidden one, e.g. <c>Name:280,Type:72,!Folder:260</c>.
    /// </summary>
    /// <param name="columns">The columns in order.</param>
    /// <returns>The text.</returns>
    public static string Serialize(IEnumerable<ListColumnViewModel> columns) => string.Join(
        ',',
        (columns ?? []).Where(column => column.IsConfigurable).Select(column =>
            $"{(column.IsVisible ? string.Empty : "!")}{column.Kind}:{Math.Round(column.Width).ToString(CultureInfo.InvariantCulture)}"));

    /// <summary>
    /// Reads a stored setup. Unknown or repeated columns and bad widths are skipped, widths are kept in range, and
    /// columns missing from the text (e.g. added in a later version) come after the others with their defaults.
    /// </summary>
    /// <param name="text">The text <see cref="Serialize"/> wrote, or anything else.</param>
    /// <returns>The columns in order, the thumbnail first.</returns>
    public static List<ListColumnViewModel> Parse(string? text)
    {
        var columns = new List<ListColumnViewModel> { Default(ListColumnKind.Thumbnail) };
        foreach (var part in (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var hidden = part.StartsWith('!');
            var pair = part.TrimStart('!').Split(':');
            if (pair.Length != 2
                || !Enum.TryParse<ListColumnKind>(pair[0], ignoreCase: false, out var kind)
                || !Enum.IsDefined(kind)
                || kind == ListColumnKind.Thumbnail
                || columns.Any(column => column.Kind == kind)
                || !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)
                || !double.IsFinite(width))
            {
                continue;
            }

            var column = Default(kind);
            column.Width = Math.Clamp(width, ListColumnViewModel.MinWidth, ListColumnViewModel.MaxWidth);
            column.IsVisible = !hidden || !column.CanHide;
            columns.Add(column);
        }

        foreach (var entry in Defaults.Where(entry => columns.All(column => column.Kind != entry.Kind)))
        {
            columns.Add(Default(entry.Kind));
        }

        return columns;
    }

    /// <summary>
    /// Sets a column's width, kept within <see cref="ListColumnViewModel.MinWidth"/>–<see cref="ListColumnViewModel.MaxWidth"/>.
    /// The thumbnail column keeps its width.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <param name="width">The width wanted.</param>
    public void Resize(ListColumnKind kind, double width)
    {
        var column = this[kind];
        if (column.IsConfigurable && double.IsFinite(width))
        {
            column.Width = Math.Clamp(Math.Round(width), ListColumnViewModel.MinWidth, ListColumnViewModel.MaxWidth);
        }
    }

    /// <summary>
    /// Shows or hides a column. The thumbnail and the name always show.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <param name="isVisible">Whether it shows.</param>
    public void SetVisible(ListColumnKind kind, bool isVisible)
    {
        var column = this[kind];
        if (column.CanHide)
        {
            column.IsVisible = isVisible;
        }
    }

    /// <summary>
    /// Moves a column to another position. The thumbnail stays first, so nothing moves before it.
    /// </summary>
    /// <param name="kind">The column.</param>
    /// <param name="index">The position wanted, from 1 (after the thumbnail).</param>
    public void Move(ListColumnKind kind, int index)
    {
        var column = this[kind];
        var from = Columns.IndexOf(column);
        var to = Math.Clamp(index, 1, Columns.Count - 1);
        if (!column.IsConfigurable || from == to)
        {
            return;
        }

        Columns.Move(from, to);
        RaiseLayoutChanged();
    }

    /// <summary>
    /// Puts every column back to its default width, position and visibility.
    /// </summary>
    [RelayCommand]
    public void Reset() => Apply(DefaultSetup());

    /// <summary>
    /// Saves a pending change now instead of after the usual delay, e.g. when the app closes, and for tests.
    /// </summary>
    public void SaveNow()
    {
        saveTimer.Stop();
        if (settings is null || !settings.IsAvailable(SettingScope.App))
        {
            return;
        }

        try
        {
            var text = Serialize(Columns);
            if (text != settings.Get(BrowserSettings.ListColumns))
            {
                settings.SetValue(BrowserSettings.ListColumns, text);
            }
        }
        catch (IOException)
        {
            // The setup is a convenience; the next change tries again.
        }
    }

    private static ListColumnViewModel Default(ListColumnKind kind)
    {
        var entry = Defaults.First(entry => entry.Kind == kind);
        return new ListColumnViewModel(entry.Kind, entry.Width, entry.IsVisible);
    }

    private static List<ListColumnViewModel> DefaultSetup() => [.. Defaults.Select(entry => Default(entry.Kind))];

    private void Apply(IReadOnlyList<ListColumnViewModel> columns)
    {
        applying = true;
        try
        {
            foreach (var column in Columns)
            {
                column.PropertyChanged -= OnColumnChanged;
            }

            Columns.Clear();
            foreach (var column in columns)
            {
                column.PropertyChanged += OnColumnChanged;
                Columns.Add(column);
            }
        }
        finally
        {
            applying = false;
        }

        RaiseLayoutChanged();
    }

    private void OnColumnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ListColumnViewModel.Width) or nameof(ListColumnViewModel.IsVisible))
        {
            RaiseLayoutChanged();
        }
    }

    private void RaiseLayoutChanged()
    {
        if (applying)
        {
            return;
        }

        OnPropertyChanged(nameof(TotalWidth));
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        if (settings is not null)
        {
            saveTimer.Stop();
            saveTimer.Start();
        }
    }
}
