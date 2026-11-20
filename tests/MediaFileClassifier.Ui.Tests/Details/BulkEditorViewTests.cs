using Avalonia.Controls;
using Avalonia.Threading;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Ui.ViewModels.Details;
using MediaFileClassifier.Ui.Views.Details;
using MediaFileClassifier.UseCases.Metadata.UndoBulkEdit;

namespace MediaFileClassifier.Ui.Tests.Details;

/// <summary>
/// Tests for the bulk editor's view (E07-S03): the Save bar's confirmation, progress and errors, and the result bar
/// with Undo, in a headless window.
/// </summary>
public sealed class BulkEditorViewTests
{
    [Fact]
    public Task SaveBar_AsksFirst_ThenSaves_AndTheResultBarOffersUndo() => TestApp.RunAsync(() =>
    {
        var (window, view, editor) = Show(40);
        Assert.False(view.FindControl<Border>("SaveBar")!.IsVisible);
        Assert.False(view.FindControl<Border>("ResultBar")!.IsVisible);

        editor.AddTag("trip");
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Border>("SaveBar")!.IsVisible);
        Assert.True(view.FindControl<Button>("SaveButton")!.IsVisible);
        Assert.False(view.FindControl<Button>("ConfirmButton")!.IsVisible);

        editor.RequestSaveCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Button>("ConfirmButton")!.IsVisible);
        Assert.False(view.FindControl<Button>("SaveButton")!.IsVisible);
        Assert.Equal("40 items will change.", view.FindControl<TextBlock>("ConfirmText")!.Text);

        editor.RequestSaveAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.False(view.FindControl<Border>("SaveBar")!.IsVisible);
        Assert.True(view.FindControl<Border>("ResultBar")!.IsVisible);
        Assert.Equal("40 items changed.", view.FindControl<TextBlock>("ResultText")!.Text);
        Assert.True(view.FindControl<Button>("UndoButton")!.IsVisible);

        editor.UndoAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Put back 40 items.", view.FindControl<TextBlock>("ResultText")!.Text);
        Assert.False(view.FindControl<Button>("UndoButton")!.IsVisible);
        window.Close();
    });

    [Fact]
    public Task SaveBar_AFailedSave_ShowsTheError_AndKeepsTheBar() => TestApp.RunAsync(() =>
    {
        var (window, view, editor) = Show(5);
        editor.AddTag("trip");
        editor.Sender.Failure = new InvalidOperationException("disk");

        editor.SaveAsync().GetAwaiter().GetResult();
        Dispatcher.UIThread.RunJobs();

        Assert.True(view.FindControl<Border>("SaveBar")!.IsVisible);
        Assert.True(view.FindControl<TextBlock>("SaveErrorText")!.IsVisible);
        Assert.False(view.FindControl<Border>("ResultBar")!.IsVisible);
        window.Close();
    });

    private static (Window Window, BulkEditorView View, ViewModelHandle Editor) Show(int count)
    {
        var sender = new BulkEditorSaveTests.SavingSender { Store = new BulkUndoStore(), WrittenWhenDone = count };
        var editor = new BulkEditorViewModel(sender, TestLocalizer.EnUs(), undoStore: sender.Store);
        editor.LoadAsync([.. Enumerable.Range(0, count).Select(_ => Id<MediaItem>.New())]).GetAwaiter().GetResult();
        var view = new BulkEditorView { DataContext = editor };
        var window = new Window { Width = 420, Height = 900, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, new ViewModelHandle(editor, sender));
    }

    // The editor with the sender that serves it, so a test can change what the save does.
    private sealed record ViewModelHandle(BulkEditorViewModel Editor, BulkEditorSaveTests.SavingSender Sender)
    {
        public void AddTag(string text) => Editor.AddTag(text);

        public System.Windows.Input.ICommand RequestSaveCommand => Editor.RequestSaveCommand;

        public Task RequestSaveAsync() => Editor.RequestSaveAsync();

        public Task SaveAsync() => Editor.SaveAsync();

        public Task UndoAsync() => Editor.UndoAsync();
    }
}
