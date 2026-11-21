using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.Ui.Views.Libraries;

namespace MediaFileClassifier.Ui.Libraries;

/// <summary>
/// The Excluded items command in the Library menu (E02-S10-T04): opens the dialog that lists the items the user
/// removed from the library, modal to the main window.
/// </summary>
/// <param name="excludedItems">Makes a fresh view model.</param>
/// <param name="localizer">UI strings.</param>
/// <param name="session">Holds the open library; the command needs one.</param>
public sealed class ExcludedItemsCommands(Func<ExcludedItemsViewModel> excludedItems, Localizer localizer, ILibrarySession? session = null) : ICommandContributor
{
    /// <summary>Command ID: show the items removed from the library.</summary>
    public const string ExcludedItems = "library.excluded-items";

    /// <inheritdoc />
    public void Register(CommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var command = registry.Add(new AppCommand(ExcludedItems, CommandGroups.Library, 125, "Command_ExcludedItems", ShowAsync)
        {
            IsVisible = LibraryDialogs.IsSupported,
            WhyDisabled = () => session?.Current is null ? localizer.Get("Command_NoLibraryOpen") : null,
        });
        if (session is not null)
        {
            session.CurrentChanged += (_, _) => Dispatcher.UIThread.Post(command.Refresh);
        }
    }

    private async Task ShowAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
        {
            return;
        }

        var viewModel = excludedItems();
        var window = new ExcludedItemsWindow { DataContext = viewModel };
        viewModel.CloseRequested += (_, _) => window.Close();
        await viewModel.LoadAsync();
        await window.ShowDialog(owner);
    }
}
