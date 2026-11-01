using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaFileClassifier.Ui.ViewModels;

namespace MediaFileClassifier.App.Android.Spike;

/// <summary>
/// The smoke-test page (MFC-18): buttons for each check, and their results. E20 replaces it with the real pages.
/// </summary>
internal sealed partial class SpikeViewModel : ViewModelBase
{
    private readonly SqliteCheck sqlite;
    private readonly OnnxCheck onnx;
    private readonly SafCheck saf;
    private readonly VectorSearchCheck vectors;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpikeViewModel"/> class.
    /// </summary>
    /// <param name="sqlite">The SQLite check.</param>
    /// <param name="onnx">The ONNX Runtime check.</param>
    /// <param name="saf">The Storage Access Framework check.</param>
    /// <param name="vectors">The in-memory vector search check.</param>
    public SpikeViewModel(SqliteCheck sqlite, OnnxCheck onnx, SafCheck saf, VectorSearchCheck vectors)
    {
        this.sqlite = sqlite;
        this.onnx = onnx;
        this.saf = saf;
        this.vectors = vectors;
        SpikeLog.LineWritten += line => Dispatcher.UIThread.Post(() => Lines.Add(line));
    }

    /// <summary>
    /// Gets the result lines.
    /// </summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>
    /// Gets a value indicating whether a check is running.
    /// </summary>
    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>
    /// Runs checks one after another.
    /// </summary>
    /// <param name="script">Checks separated by <c>;</c>: <c>sqlite</c>, <c>saf</c>, <c>vectors:&lt;count&gt;</c> or <c>onnx:&lt;model&gt;:&lt;provider&gt;</c>.</param>
    /// <returns>A task that completes when all checks are done.</returns>
    public async Task RunScriptAsync(string script)
    {
        foreach (var step in script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            await RunAsync(step);
        }

        SpikeLog.Write("script: done");
    }

    [RelayCommand]
    private Task RunAsync(string check)
    {
        var parts = check.Split(':');
        return Guard(check, parts[0] switch
        {
            "sqlite" => sqlite.RunAsync,
            "saf" => saf.RunAsync,
            "vectors" when parts.Length == 2 => () => vectors.RunAsync(int.Parse(parts[1], CultureInfo.InvariantCulture)),
            "onnx" when parts.Length == 3 => () => onnx.RunAsync(parts[1], parts[2]),
            _ => () => throw new ArgumentException($"Unknown check '{check}'."),
        });
    }

    private async Task Guard(string check, Func<Task> run)
    {
        IsBusy = true;
        try
        {
            await run();
        }
        catch (Exception ex)
        {
            SpikeLog.Write($"{check}: FAILED {ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
