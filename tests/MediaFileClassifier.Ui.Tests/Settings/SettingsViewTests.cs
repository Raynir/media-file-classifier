using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Settings;
using MediaFileClassifier.Ui.ViewModels.Settings;
using MediaFileClassifier.Ui.Views.Settings;
using MediaFileClassifier.UseCases;
using Microsoft.Extensions.DependencyInjection;

namespace MediaFileClassifier.Ui.Tests.Settings;

/// <summary>
/// Tests for the settings window and its command (E17-S01-T02).
/// </summary>
public sealed class SettingsViewTests
{
    [Fact]
    public Task Window_ShowsAControlForEachSettingOfTheCategory() => TestApp.RunAsync(() =>
    {
        using var services = CreateServices();
        var viewModel = services.GetRequiredService<SettingsViewModel>();
        var window = CreateWindow(services, viewModel);
        window.Show();

        var controls = new List<Control>();
        foreach (var category in viewModel.Categories)
        {
            viewModel.SelectedCategory = category;
            Dispatcher.UIThread.RunJobs();
            controls.AddRange(window.GetLogicalDescendants().OfType<Control>()
                .Where(control => control is ToggleSwitch or NumericUpDown or TextBox or ComboBox && control.DataContext is SettingItemViewModel));
        }

        window.Close();
        Assert.Contains(controls, control => control is ComboBox);
        Assert.Contains(controls, control => control is TextBox);
        Assert.Contains(controls, control => control is ToggleSwitch);
        Assert.Contains(controls, control => control is NumericUpDown);
    });

    [Fact]
    public Task TextBox_LosingFocus_SavesTheText() => TestApp.RunAsync(() =>
    {
        using var services = CreateServices();
        var viewModel = services.GetRequiredService<SettingsViewModel>();
        viewModel.SelectedCategory = viewModel.Categories.Single(category => category.Category == SettingCategories.AiModels);
        var window = CreateWindow(services, viewModel);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var textBox = window.GetLogicalDescendants().OfType<TextBox>().Single(box => box.DataContext is TextSettingItemViewModel);

        textBox.Focus();
        textBox.Text = "http://localhost:11434";
        window.GetLogicalDescendants().OfType<ListBox>().Single().Focus();
        Dispatcher.UIThread.RunJobs();

        window.Close();
        Assert.Equal("http://localhost:11434", services.GetRequiredService<ISettingsStore>().Get(KnownSettings.OpenAiCompatibleEndpoint));
    });

    [Fact]
    public void SettingsCommand_IsInTheToolsMenuWithCtrlComma()
    {
        using var services = CreateServices();

        var command = services.GetRequiredService<CommandRegistry>()[SettingsCommands.OpenSettings];

        Assert.Equal(CommandGroups.Tools, command.Group);
        Assert.Equal(new KeyGesture(Key.OemComma, KeyModifiers.Control), command.Gesture);
        Assert.Equal("Icon.Settings", command.IconKey);
    }

    // The test app has no services, so the window gets the view locator the real app adds to its data templates.
    private static SettingsWindow CreateWindow(ServiceProvider services, SettingsViewModel viewModel)
    {
        var window = new SettingsWindow { DataContext = viewModel };
        window.DataTemplates.Add(services.GetRequiredService<ViewLocator>());
        return window;
    }

    private static ServiceProvider CreateServices() => new ServiceCollection()
        .AddSingleton<ISettingsStore>(new InMemorySettingsStore())
        .AddSingleton<Infrastructure.Abstractions.Paths.IAppPaths>(new Commands.FakeAppPaths())
        .AddSingleton<Platform.ICapabilityGate>(new Commands.FakeCapabilityGate())
        .AddUseCases()
        .AddUi()
        .BuildServiceProvider();
}
