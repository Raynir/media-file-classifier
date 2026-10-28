namespace MediaFileClassifier.Ui;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using MediaFileClassifier.Ui.ViewModels;

/// <summary>
/// Builds the view for a view model from explicit registrations, without reflection,
/// so it keeps working when the app is trimmed (Android, E20).
/// </summary>
public sealed class ViewLocator : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> factories = [];

    /// <summary>
    /// Maps a view model type to the view that displays it.
    /// </summary>
    /// <typeparam name="TViewModel">View model type.</typeparam>
    /// <typeparam name="TView">View type.</typeparam>
    /// <returns>The same locator, for chaining.</returns>
    public ViewLocator Register<TViewModel, TView>()
        where TViewModel : ViewModelBase
        where TView : Control, new()
    {
        factories[typeof(TViewModel)] = () => new TView();
        return this;
    }

    /// <inheritdoc />
    public Control? Build(object? param)
    {
        if (param is null)
        {
            return null;
        }

        return factories.TryGetValue(param.GetType(), out var factory)
            ? factory()
            : new TextBlock { Text = $"No view registered for {param.GetType().Name}." };
    }

    /// <inheritdoc />
    public bool Match(object? data) => data is ViewModelBase;
}
