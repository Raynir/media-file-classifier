using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.Settings;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Shortcuts;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.Ui.ViewModels.Browser;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Exclusions.ExcludeItems;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the Remove from library command (E02-S10-T03): one confirmation for the whole selection, which
/// "Don't ask again" turns off through a registered setting.
/// </summary>
public sealed class RemoveItemsCommandsTests
{
    [Fact]
    public void Menu_WithNothingSelected_LeavesRemoveOut()
    {
        var bed = new Bed(5);

        Assert.DoesNotContain(bed.Menu.Build().OfType<AppCommand>(), command => command.Id == RemoveItemsCommands.Remove);
    }

    [Fact]
    public async Task Menu_ForASelection_ListsRemove()
    {
        var bed = new Bed(5);
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        Assert.Contains(bed.Menu.Build().OfType<AppCommand>(), command => command.Id == RemoveItemsCommands.Remove);
    }

    [Fact]
    public void Keymap_BindsDeleteInTheBrowser()
    {
        Assert.Contains(
            DefaultKeymap.Bindings,
            binding => binding.CommandId == RemoveItemsCommands.Remove
                && binding.Chord == new KeyChord(Avalonia.Input.PhysicalKey.Delete, Avalonia.Input.KeyModifiers.None)
                && binding.Scope == ShortcutScopes.Browser);
    }

    [Fact]
    public async Task Execute_AsksOnceWithTheCount_ThenRemovesTheWholeSelection()
    {
        var bed = new Bed(5);
        await bed.Selection.ApplyAsync(0, SelectionGesture.Replace);
        await bed.Selection.ApplyAsync(2, SelectionGesture.Range);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        var asked = Assert.Single(bed.Dialog.Asked);
        Assert.Equal("Remove 3 items from the library?", asked.Question);
        var sent = Assert.Single(bed.Removals);
        Assert.Equal(bed.Source.Ids.Take(3).ToHashSet(), sent.ItemIds.ToHashSet());
        Assert.True(bed.Settings.Get(RemoveItemsCommands.AskBeforeRemoving));
    }

    [Fact]
    public async Task Execute_Cancelled_RemovesNothing()
    {
        var bed = new Bed(5);
        bed.Dialog.Answer = false;
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        Assert.Equal("Remove 1 item from the library?", Assert.Single(bed.Dialog.Asked).Question);
        Assert.Empty(bed.Removals);
    }

    [Fact]
    public async Task Execute_DontAskAgain_TurnsTheSettingOff_AndTheNextRemovalDoesNotAsk()
    {
        var bed = new Bed(5);
        bed.Dialog.DontAskAgain = true;
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();
        await bed.Selection.ApplyAsync(2, SelectionGesture.Replace);
        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        Assert.False(bed.Settings.Get(RemoveItemsCommands.AskBeforeRemoving));
        Assert.Single(bed.Dialog.Asked);
        Assert.Equal(2, bed.Removals.Count);
    }

    [Fact]
    public async Task Execute_DontAskAgainButCancelled_KeepsAsking()
    {
        var bed = new Bed(5);
        bed.Dialog.Answer = false;
        bed.Dialog.DontAskAgain = true;
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        Assert.True(bed.Settings.Get(RemoveItemsCommands.AskBeforeRemoving));
        Assert.Empty(bed.Removals);
    }

    [Fact]
    public async Task Execute_WithNothingSelected_DoesNothing()
    {
        var bed = new Bed(5);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        Assert.Empty(bed.Dialog.Asked);
        Assert.Empty(bed.Removals);
    }

    [Fact]
    public async Task Execute_WhenTheStoreFails_DoesNotThrow()
    {
        var bed = new Bed(5) { Failure = new IOException("The library is locked.") };
        await bed.Selection.ApplyAsync(1, SelectionGesture.Replace);

        await bed.Registry[RemoveItemsCommands.Remove].ExecuteAsync();

        Assert.Empty(bed.Removals);
    }

    /// <summary>
    /// A selection over fake results, with the Remove command, a scripted confirmation and the context menu.
    /// </summary>
    private sealed class Bed
    {
        public Bed(int count)
        {
            Source = new FakeResultSource(count);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(count);
            var commands = new RemoveItemsCommands(Selection, new RemoveSender(this), Settings, Dialog, Localizer.Current);
            Registry = new CommandRegistry([commands], Localizer.Current, new FakeCapabilityGate());
            Menu = new BrowserContextMenu(Registry, Localizer.Current);
        }

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public InMemorySettingsStore Settings { get; } = new();

        public ScriptedDialog Dialog { get; } = new();

        public CommandRegistry Registry { get; }

        public BrowserContextMenu Menu { get; }

        public Exception? Failure { get; init; }

        public List<ExcludeItemsCommand> Removals { get; } = [];
    }

    private sealed class ScriptedDialog : IRemoveItemsDialog
    {
        public bool Answer { get; set; } = true;

        public bool DontAskAgain { get; set; }

        public List<RemoveItemsConfirmationViewModel> Asked { get; } = [];

        public Task<bool> AskAsync(RemoveItemsConfirmationViewModel confirmation)
        {
            Asked.Add(confirmation);
            confirmation.DontAskAgain = DontAskAgain;
            return Task.FromResult(Answer);
        }
    }

    private sealed class RemoveSender(Bed bed) : FakeSender
    {
        protected override object? Handle(object request) => request switch
        {
            ResolveItemSelectionQuery query => (IReadOnlyList<Id<MediaItem>>)[.. bed.Source.Ids.Where(query.Selection.Contains)],
            ExcludeItemsCommand command => Remove(command),
            _ => throw new NotSupportedException(request.GetType().Name),
        };

        private ExcludeItemsResult Remove(ExcludeItemsCommand command)
        {
            if (bed.Failure is { } failure)
            {
                throw failure;
            }

            bed.Removals.Add(command);
            return new ExcludeItemsResult(command.ItemIds.Count, CleanupQueued: false);
        }
    }
}
