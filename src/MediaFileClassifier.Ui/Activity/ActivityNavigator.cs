using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Libraries;
using MediaFileClassifier.Ui.Navigation;
using MediaFileClassifier.Ui.ViewModels.Navigation;
using MediaFileClassifier.UseCases.Browsing;
using MediaFileClassifier.UseCases.Browsing.LibraryItems;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Activity;

/// <summary>
/// Selects the folder in the navigation panel, so the browser and the panel agree, and falls back to setting the
/// browser's scope when the panel doesn't list the folder yet. Use it on the UI thread.
/// </summary>
/// <param name="services">Resolves the navigation lazily, which keeps the window out of the navigation's construction.</param>
public sealed class ActivityNavigator(IServiceProvider services) : IActivityNavigator
{
    /// <inheritdoc />
    public void ShowFolder(Id<IndexedFolder> folderId)
    {
        var navigation = services.GetRequiredService<NavigationPanelViewModel>();
        var entry = navigation.Sections
            .FirstOrDefault(section => section.Key == FolderNavigation.SectionKey)?
            .Entries.FirstOrDefault(candidate => candidate.Key == folderId.ToString());
        if (entry is not null)
        {
            navigation.Select(entry);
        }
        else
        {
            services.GetRequiredService<LibraryItemsSource>().Scope = new FolderScope(folderId);
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } main })
        {
            main.Activate();
        }
    }
}
