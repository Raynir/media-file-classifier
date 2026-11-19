using MediaFileClassifier.Ui.Editing;

namespace MediaFileClassifier.Ui.Tests.Editing;

/// <summary>
/// Tests for the <see cref="UnsavedChangesGuard"/> (E07-S12-T01): the Save / Discard / Stay prompt for staged editors.
/// </summary>
public sealed class UnsavedChangesGuardTests
{
    private readonly UnsavedChangesGuard guard = new(TestLocalizer.EnUs());
    private readonly List<string> prompts = [];
    private UnsavedChangesChoice answer = UnsavedChangesChoice.Stay;

    public UnsavedChangesGuardTests()
    {
        guard.Ask = prompt =>
        {
            prompts.Add(prompt.Message);
            return Task.FromResult(answer);
        };
    }

    [Fact]
    public async Task Confirm_WithNothingStaged_AllowsAndAsksNothing()
    {
        guard.Register(new FakeEditor("A"));

        Assert.True(await guard.ConfirmLeaveAsync());
        Assert.Empty(prompts);
        Assert.False(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task Save_SavesTheEditor_AndAllows()
    {
        var editor = new FakeEditor("Beach day") { HasUnsavedChanges = true };
        guard.Register(editor);
        answer = UnsavedChangesChoice.Save;

        Assert.True(await guard.ConfirmLeaveAsync());

        Assert.Equal(["You have unsaved changes to \"Beach day\". Save them before you go on?"], prompts);
        Assert.Equal((1, 0), (editor.Saves, editor.Discards));
    }

    [Fact]
    public async Task Save_ThatFails_KeepsTheChanges_AndRefusesToLeave()
    {
        var editor = new FakeEditor("A") { HasUnsavedChanges = true, SaveSucceeds = false };
        guard.Register(editor);
        answer = UnsavedChangesChoice.Save;

        Assert.False(await guard.ConfirmLeaveAsync());

        Assert.True(editor.HasUnsavedChanges);
        Assert.True(guard.HasUnsavedChanges);
    }

    [Fact]
    public async Task Discard_DropsTheChanges_AndAllows()
    {
        var editor = new FakeEditor("A") { HasUnsavedChanges = true };
        guard.Register(editor);
        answer = UnsavedChangesChoice.Discard;

        Assert.True(await guard.ConfirmLeaveAsync());

        Assert.Equal((0, 1), (editor.Saves, editor.Discards));
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task Stay_RefusesToLeave_AndKeepsTheChanges()
    {
        var editor = new FakeEditor("A") { HasUnsavedChanges = true };
        guard.Register(editor);

        Assert.False(await guard.ConfirmLeaveAsync());

        Assert.Equal((0, 0), (editor.Saves, editor.Discards));
        Assert.True(editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task EveryEditorWithChanges_IsAsked_AndStayOnTheFirstStopsTheRest()
    {
        var first = new FakeEditor("First") { HasUnsavedChanges = true };
        var second = new FakeEditor("Second") { HasUnsavedChanges = true };
        guard.Register(first);
        guard.Register(second);
        answer = UnsavedChangesChoice.Discard;

        Assert.True(await guard.ConfirmLeaveAsync());
        Assert.Equal(2, prompts.Count);

        first.HasUnsavedChanges = true;
        second.HasUnsavedChanges = true;
        prompts.Clear();
        answer = UnsavedChangesChoice.Stay;

        Assert.False(await guard.ConfirmLeaveAsync());
        Assert.Single(prompts);
    }

    [Fact]
    public async Task UnregisteredEditor_IsNotAsked()
    {
        var registration = guard.Register(new FakeEditor("A") { HasUnsavedChanges = true });

        registration.Dispose();

        Assert.True(await guard.ConfirmLeaveAsync());
        Assert.Empty(prompts);
    }

    [Fact]
    public async Task Confirm_WhileAPromptIsOpen_RefusesWithoutAskingAgain()
    {
        guard.Register(new FakeEditor("A") { HasUnsavedChanges = true });
        var open = new TaskCompletionSource<UnsavedChangesChoice>();
        guard.Ask = _ => open.Task;

        var first = guard.ConfirmLeaveAsync();
        var second = await guard.ConfirmLeaveAsync();
        open.SetResult(UnsavedChangesChoice.Discard);

        Assert.False(second);
        Assert.True(await first);
    }

    private sealed class FakeEditor(string description) : IStagedEditor
    {
        public bool HasUnsavedChanges { get; set; }

        public string Description { get; } = description;

        public bool SaveSucceeds { get; set; } = true;

        public int Saves { get; private set; }

        public int Discards { get; private set; }

        public Task<bool> SaveAsync()
        {
            Saves++;
            if (SaveSucceeds)
            {
                HasUnsavedChanges = false;
            }

            return Task.FromResult(SaveSucceeds);
        }

        public void Discard()
        {
            Discards++;
            HasUnsavedChanges = false;
        }
    }
}
