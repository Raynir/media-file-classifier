using MediaFileClassifier.Infrastructure.Abstractions.Files;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Infrastructure.Files;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Maintenance;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// Composition tests for the Settings "Library" page (E17-S04): the real container's sections, in order, and their views.
/// </summary>
public sealed class LibrarySettingsPageTests : IDisposable
{
    private readonly ServiceProvider services = new ServiceCollection()
        .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
        .AddSingleton<Infrastructure.Abstractions.Paths.IAppPaths>(new FakeAppPaths())
        .AddSingleton<Platform.ICapabilityGate>(new FakeCapabilityGate())
        .AddSingleton<Infrastructure.Abstractions.Platform.IPlatformServices>(new FakePlatformServices())
        .AddSingleton<Infrastructure.Abstractions.Jobs.IJobProgress>(new FakeJobProgress())
        .AddSingleton<ILibrarySession>(new FakeBrowserSession(false))
        .AddSingleton<IMediaFileSource, FileSystemMediaFileSource>()
        .AddSingleton<Infrastructure.Abstractions.Formats.IMediaFormatRegistry>(new Infrastructure.Media.Formats.MediaFormatRegistry([]))
        .AddLogging()
        .AddUseCases()
        .AddUi()
        .BuildServiceProvider();

    public void Dispose() => services.Dispose();

    [Fact]
    public void LibraryCategory_HostsEveryLibraryOperation_InOrder()
    {
        var category = services.GetRequiredService<SettingsViewModel>().Categories.Single(c => c.Category == SettingCategories.Library);

        var sections = category.Entries.OfType<CustomSettingsSectionViewModel>().Select(section => section.Content.GetType()).ToList();

        Assert.Equal(
            [typeof(LibraryInformationSectionViewModel), typeof(LibraryFoldersSectionViewModel), typeof(LibraryMaintenanceSectionViewModel), typeof(LibraryExcludedItemsSectionViewModel)],
            sections);
        // The sections of the page come around the settings other Stories add: information and folders first.
        Assert.IsType<CustomSettingsSectionViewModel>(category.Entries[0]);
        Assert.IsType<CustomSettingsSectionViewModel>(category.Entries[1]);
        Assert.IsType<CustomSettingsSectionViewModel>(category.Entries[^1]);
    }

    [Fact]
    public void EverySection_HasAView()
    {
        var locator = services.GetRequiredService<ViewLocator>();
        var category = services.GetRequiredService<SettingsViewModel>().Categories.Single(c => c.Category == SettingCategories.Library);

        foreach (var section in category.Entries.OfType<CustomSettingsSectionViewModel>())
        {
            Assert.DoesNotContain("No view registered", (locator.Build(section.Content) as Avalonia.Controls.TextBlock)?.Text ?? string.Empty);
        }
    }

    [Fact]
    public void Maintenance_OffersTheLibraryMenuCommands_DisabledWithoutALibrary()
    {
        var maintenance = services.GetRequiredService<LibraryMaintenanceSectionViewModel>();

        Assert.Equal(MaintenanceCommands.RefreshIndex, maintenance.RefreshIndex.Id);
        Assert.Equal(MaintenanceCommands.RegenerateThumbnails, maintenance.RegenerateThumbnails.Id);
        Assert.Equal(MaintenanceCommands.CheckIntegrity, maintenance.CheckIntegrity.Id);
        Assert.All([maintenance.RefreshIndex, maintenance.RegenerateThumbnails, maintenance.CheckIntegrity], command => Assert.False(command.IsEnabled));
    }
}
