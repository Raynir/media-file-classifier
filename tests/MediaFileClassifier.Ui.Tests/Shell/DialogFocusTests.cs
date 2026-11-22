using Avalonia.Controls;
using MediaFileClassifier.Ui.Shell;
using MediaFileClassifier.Ui.ViewModels;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.Ui.Views;
using MediaFileClassifier.Ui.Views.Browser;

namespace MediaFileClassifier.Ui.Tests.Shell;

/// <summary>
/// Tests for <see cref="DialogFocus"/>: a dialog focuses its first input or its default button when it opens (MFC-1012).
/// </summary>
public sealed class DialogFocusTests
{
    [Fact]
    public Task RemoveItems_FocusesTheSafeDefaultButton_Cancel() => RunAsync(
        new RemoveItemsConfirmationView { DataContext = new RemoveItemsConfirmationViewModel(2, TestLocalizer.EnUs()) },
        focused => Assert.Equal("CancelButton", focused.Name));

    [Fact]
    public Task QuitConfirmation_FocusesTheDefaultButton() => RunAsync(
        new QuitConfirmationView { DataContext = new QuitConfirmationViewModel(1, TestLocalizer.EnUs()) },
        focused => Assert.True(Assert.IsType<Button>(focused).IsDefault));

    [Fact]
    public Task UnsavedChanges_FocusesTheDefaultButton() => RunAsync(
        new UnsavedChangesView { DataContext = new UnsavedChangesViewModel("one item", TestLocalizer.EnUs()) },
        focused => Assert.True(Assert.IsType<Button>(focused).IsDefault));

    [Fact]
    public Task AnInput_WinsOverTheDefaultButton() => RunAsync(
        new StackPanel { Children = { new Button { Content = "OK", IsDefault = true }, new TextBox { Name = "Box" } } },
        focused => Assert.Equal("Box", focused.Name));

    [Fact]
    public Task AControlThatAlreadyHasTheFocus_Keeps_It() => RunAsync(
        new StackPanel { Children = { new TextBox { Name = "First" }, new TextBox { Name = "Second" } } },
        focused => Assert.Equal("Second", focused.Name),
        beforehand: content => ((StackPanel)content).Children[1].Focus());

    private static Task RunAsync(Control content, Action<Control> assert, Action<Control>? beforehand = null) => TestApp.RunAsync(() =>
    {
        var window = new Window { Content = content, Width = 400, Height = 300 };
        window.Show();
        beforehand?.Invoke(content);

        var focused = DialogFocus.FocusInitial(window);

        Assert.NotNull(focused);
        assert(focused);
        Assert.Same(focused, window.FocusManager?.GetFocusedElement());
        window.Close();
    });
}
