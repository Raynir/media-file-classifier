using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Commands;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Tests.Commands;
using MediaFileClassifier.UseCases.Browsing.Selection;
using MediaFileClassifier.UseCases.Favorites.GetFavoriteState;
using MediaFileClassifier.UseCases.Favorites.ToggleFavorite;

namespace MediaFileClassifier.Ui.Tests.Browser;

/// <summary>
/// Tests for the Favorite command (E11-S01-T02): in the context menu for any selection, checked while every selected
/// item is a favorite, and toggling the whole selection.
/// </summary>
public sealed class FavoriteCommandsTests
{
    [Fact]
    public async Task Menu_WithNothingSelected_LeavesFavoriteOut()
    {
        var bed = new Bed(5);

        Assert.DoesNotContain(bed.Menu.Build().OfType<AppCommand>(), command => command.Id == FavoriteCommands.Favorite);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Menu_ForASelection_ShowsFavoriteUnchecked_UntilAllSelectedAreFavorites()
    {
        var bed = new Bed(5);
        await bed.SelectAsync(1);
        await bed.Selection.ApplyAsync(2, SelectionGesture.Toggle);
        bed.Favorites.Add(bed.Source.Ids[1]);

        bed.Commands.Reload();
        await bed.Commands.Loading;
        var mixed = bed.Menu.Build().OfType<AppCommand>().Single(command => command.Id == FavoriteCommands.Favorite);
        Assert.False(mixed.IsChecked);

        bed.Favorites.Add(bed.Source.Ids[2]);
        bed.Commands.Reload();
        await bed.Commands.Loading;
        var all = bed.Menu.Build().OfType<AppCommand>().Single(command => command.Id == FavoriteCommands.Favorite);
        Assert.True(all.IsChecked);
    }

    [Fact]
    public async Task Execute_TogglesEverySelectedItem_InOneCommand()
    {
        var bed = new Bed(5);
        await bed.SelectAsync(0);
        await bed.Selection.ApplyAsync(3, SelectionGesture.Range);

        await bed.Registry[FavoriteCommands.Favorite].ExecuteAsync();

        var sent = Assert.Single(bed.Toggles);
        Assert.Equal(bed.Source.Ids.Take(4).ToHashSet(), sent.ItemIds.ToHashSet());
    }

    [Fact]
    public async Task Execute_WithNothingSelected_DoesNothing()
    {
        var bed = new Bed(5);

        await bed.Registry[FavoriteCommands.Favorite].ExecuteAsync();

        Assert.Empty(bed.Toggles);
    }

    /// <summary>
    /// A selection over fake results, with the Favorite command and the context menu around it.
    /// </summary>
    private sealed class Bed
    {
        public Bed(int count)
        {
            Source = new FakeResultSource(count);
            Selection = new SelectionModel(() => Source);
            Selection.KnownId = index => index >= 0 && index < Source.Ids.Count ? Source.Ids[index] : null;
            Selection.Reset(count);
            Commands = new FavoriteCommands(Selection, new FavoriteSender(this));
            Registry = new CommandRegistry([Commands], Localizer.Current, new FakeCapabilityGate());
            Menu = new BrowserContextMenu(Registry, Localizer.Current);
        }

        public FakeResultSource Source { get; }

        public SelectionModel Selection { get; }

        public FavoriteCommands Commands { get; }

        public CommandRegistry Registry { get; }

        public BrowserContextMenu Menu { get; }

        public HashSet<Id<MediaItem>> Favorites { get; } = [];

        public List<ToggleFavoriteCommand> Toggles { get; } = [];

        public async Task SelectAsync(int index)
        {
            await Selection.ApplyAsync(index, SelectionGesture.Replace);
            await Commands.Loading;
        }
    }

    private sealed class FavoriteSender(Bed bed) : FakeSender
    {
        protected override object? Handle(object request) => request switch
        {
            ResolveItemSelectionQuery query => (IReadOnlyList<Id<MediaItem>>)[.. bed.Source.Ids.Where(query.Selection.Contains)],
            GetFavoriteStateQuery query => query.ItemIds.Count > 0 && query.ItemIds.All(bed.Favorites.Contains),
            ToggleFavoriteCommand command => Toggle(command),
            _ => throw new NotSupportedException(request.GetType().Name),
        };

        private bool? Toggle(ToggleFavoriteCommand command)
        {
            bed.Toggles.Add(command);
            return true;
        }
    }
}
