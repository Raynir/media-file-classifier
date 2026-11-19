using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.Views.Details;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the metadata editor's view (E07-S02): the keyboard and the buttons on the tag chips, in a headless window.
/// </summary>
public sealed class MetadataEditorViewTests
{
    private readonly MetadataEditorSender sender = new();

    [Fact]
    public Task TagInput_EnterAddsTheTypedTag_AndTheChipShowsTheStagedState() => TestApp.RunAsync(() =>
    {
        var (window, view, editor) = Show();
        var input = view.FindControl<AutoCompleteBox>("TagInput")!;
        input.Focus();

        window.KeyTextInput("fresh");
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(TagStage.Added, editor.Tags[^1].Stage);
        Assert.Equal(string.Empty, editor.NewTagText);
        Assert.True(view.FindControl<Border>("SaveBar")!.IsVisible);
        window.Close();
    });

    [Fact]
    public Task Chips_DeleteRemovesAUserTag_ShiftDeleteRejects_BackspaceInTheEmptyInputSelectsTheLastChip() => TestApp.RunAsync(() =>
    {
        var (window, view, editor) = Show();
        var input = view.FindControl<AutoCompleteBox>("TagInput")!;
        input.Focus();

        window.KeyPressQwerty(PhysicalKey.Backspace, RawInputModifiers.None);

        var chips = view.FindControl<ItemsControl>("TagList")!.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("Chip")).ToList();
        Assert.True(chips[^1].IsFocused);

        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(TagStage.Rejected, editor.Tags[^1].Stage);

        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        window.KeyPressQwerty(PhysicalKey.Delete, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(TagStage.Removed, editor.Tags[0].Stage);
        window.Close();
    });

    private (Window Window, MetadataEditorView View, MetadataEditorViewModel Editor) Show()
    {
        var editor = new MetadataEditorViewModel(sender, TestLocalizer.EnUs());
        editor.LoadAsync(MetadataEditorTestData.ItemId).GetAwaiter().GetResult();
        var view = new MetadataEditorView { DataContext = editor };
        var window = new Window { Width = 400, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }
}
