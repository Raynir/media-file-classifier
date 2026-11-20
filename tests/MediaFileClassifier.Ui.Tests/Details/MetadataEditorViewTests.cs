using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Metadata;
using MediaFileClassifier.Ui.Details;
using MediaFileClassifier.UseCases.Metadata;
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

    [Fact]
    public Task Marks_ShowThePillOfEachFieldState_AndTheSuggestionStripWithItsButtons() => TestApp.RunAsync(() =>
    {
        var fields = MetadataEditorTestData.Sample().Fields.ToArray();
        fields[(int)DescriptiveField.Title] = fields[(int)DescriptiveField.Title] with
        {
            Suggestion = new AiFieldValue(Id<AnalysisRun>.New(), MetadataEditorTestData.ItemId, DescriptiveField.Title, FieldValue.FromText("Dog"), Id<ModelRegistration>.New(), null, DateTimeOffset.UnixEpoch),
        };
        sender.View = MetadataEditorTestData.Sample() with { Fields = fields };
        var (window, view, editor) = Show();

        var pills = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("Provenance") && border.IsEffectivelyVisible).ToList();

        // Title: Edited and New suggestion; Description, Objects and the transcript: AI.
        Assert.Equal(1, pills.Count(pill => pill.Classes.Contains("Edited")));
        Assert.Equal(1, pills.Count(pill => pill.Classes.Contains("NewSuggestion")));
        Assert.Equal(3, pills.Count(pill => pill.Classes.Contains("Ai")));
        var strip = view.GetVisualDescendants().OfType<Border>().Single(border => border.Classes.Contains("Suggestion") && border.IsEffectivelyVisible);
        var buttons = strip.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).ToList();
        Assert.Equal(2, buttons.Count);

        buttons[0].Command!.Execute(null);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.Descriptive[0].IsSuggestionAccepted);
        window.Close();
    });

    [Fact]
    public Task TagChip_EnterAcceptsASuggestion_ShiftEnterDismissesIt() => TestApp.RunAsync(() =>
    {
        sender.View = MetadataEditorTestData.Sample() with
        {
            Tags =
            [
                new TagMetadataView(new ResolvedTag(TagName.From("sea"), MetadataSource.AI, null, null, null, 0.8), TagSuggestionKind.Added),
                new TagMetadataView(new ResolvedTag(TagName.From("sky"), MetadataSource.AI, null, null, null, 0.8), TagSuggestionKind.Added),
            ],
        };
        var (window, view, editor) = Show();
        var chips = view.FindControl<ItemsControl>("TagList")!.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("Chip")).ToList();

        chips[0].Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        chips[1].Focus();
        window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.Shift);
        Dispatcher.UIThread.RunJobs();

        Assert.True(editor.Tags[0].IsSuggestionAccepted);
        Assert.True(editor.Tags[1].IsSuggestionDismissed);
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
