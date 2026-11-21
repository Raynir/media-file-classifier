using MediaFileClassifier.Domain.Identity;
using MediaFileClassifier.Domain.Media;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Exclusions.ForgetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.GetExcludedItems;
using MediaFileClassifier.UseCases.Exclusions.RestoreExcludedItems;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for the Excluded items view (E02-S10-T04): the list, Restore, and Forget after an inline confirmation.
/// </summary>
public sealed class ExcludedItemsViewModelTests
{
    [Fact]
    public async Task Load_WithNothingRemoved_IsEmpty_AndNothingCanBeDone()
    {
        var bed = new Bed(0);

        await bed.ViewModel.LoadAsync();

        Assert.True(bed.ViewModel.IsEmpty);
        Assert.False(bed.ViewModel.RestoreCommand.CanExecute(null));
        Assert.False(bed.ViewModel.ForgetCommand.CanExecute(null));
    }

    [Fact]
    public async Task Load_ListsTheItems_WithACountHeader()
    {
        var bed = new Bed(3);

        await bed.ViewModel.LoadAsync();

        Assert.Equal(3, bed.ViewModel.Items.Count);
        Assert.Equal("photo-0.jpg", bed.ViewModel.Items[0].FileName);
        Assert.Equal("3 items removed from the library. Their files are still on disk.", bed.ViewModel.Header);
    }

    [Fact]
    public async Task Restore_SendsTheSelection_AndListsAgain()
    {
        var bed = new Bed(3);
        await bed.ViewModel.LoadAsync();
        bed.ViewModel.SelectedItems = [bed.ViewModel.Items[0], bed.ViewModel.Items[2]];

        await bed.ViewModel.RestoreCommand.ExecuteAsync(null);

        var sent = Assert.Single(bed.Restores);
        Assert.Equal([bed.Ids[0], bed.Ids[2]], sent.ItemIds);
        Assert.Single(bed.ViewModel.Items);
        Assert.False(bed.ViewModel.HasSelection);
    }

    [Fact]
    public async Task Forget_AsksFirst_AndOnlyConfirmingForgets()
    {
        var bed = new Bed(2);
        await bed.ViewModel.LoadAsync();
        bed.ViewModel.SelectedItems = [bed.ViewModel.Items[1]];

        bed.ViewModel.ForgetCommand.Execute(null);

        Assert.True(bed.ViewModel.IsConfirmingForget);
        Assert.StartsWith("Forget 1 item?", bed.ViewModel.ForgetWarning, StringComparison.Ordinal);
        Assert.False(bed.ViewModel.RestoreCommand.CanExecute(null));
        Assert.Empty(bed.Forgets);

        await bed.ViewModel.ConfirmForgetCommand.ExecuteAsync(null);

        Assert.False(bed.ViewModel.IsConfirmingForget);
        Assert.Equal([bed.Ids[1]], Assert.Single(bed.Forgets).ItemIds);
        Assert.Single(bed.ViewModel.Items);
    }

    [Fact]
    public async Task Forget_Cancelled_ForgetsNothing()
    {
        var bed = new Bed(2);
        await bed.ViewModel.LoadAsync();
        bed.ViewModel.SelectedItems = [.. bed.ViewModel.Items];

        bed.ViewModel.ForgetCommand.Execute(null);
        bed.ViewModel.CancelForgetCommand.Execute(null);

        Assert.False(bed.ViewModel.IsConfirmingForget);
        Assert.Empty(bed.Forgets);
        Assert.Equal(2, bed.ViewModel.Items.Count);
    }

    [Fact]
    public async Task Restore_WhenItFails_ShowsAnErrorAndKeepsTheList()
    {
        var bed = new Bed(2) { Failure = new IOException("The library is locked.") };
        await bed.ViewModel.LoadAsync();
        bed.ViewModel.SelectedItems = [bed.ViewModel.Items[0]];

        await bed.ViewModel.RestoreCommand.ExecuteAsync(null);

        Assert.True(bed.ViewModel.HasError);
        Assert.Equal(2, bed.ViewModel.Items.Count);
    }

    [Fact]
    public void Close_RaisesCloseRequested()
    {
        var bed = new Bed(0);
        var closed = false;
        bed.ViewModel.CloseRequested += (_, _) => closed = true;

        bed.ViewModel.CloseCommand.Execute(null);

        Assert.True(closed);
    }

    /// <summary>
    /// The view model over a fake library of removed items.
    /// </summary>
    private sealed class Bed
    {
        public Bed(int count)
        {
            Ids = [.. Enumerable.Range(0, count).Select(_ => Id<MediaItem>.New())];
            Removed = [.. Ids];
            ViewModel = new ExcludedItemsViewModel(new ExcludedSender(this), Localizer.Current);
        }

        public List<Id<MediaItem>> Ids { get; }

        public List<Id<MediaItem>> Removed { get; }

        public ExcludedItemsViewModel ViewModel { get; }

        public Exception? Failure { get; init; }

        public List<RestoreExcludedItemsCommand> Restores { get; } = [];

        public List<ForgetExcludedItemsCommand> Forgets { get; } = [];
    }

    private sealed class ExcludedSender(Bed bed) : FakeSender
    {
        protected override object? Handle(object request)
        {
            switch (request)
            {
                case GetExcludedItemsQuery:
                    return (IReadOnlyList<ExcludedItem>)[.. bed.Removed.Select(id => new ExcludedItem(id, $"photo-{bed.Ids.IndexOf(id)}.jpg", @"C:\Photos", DateTimeOffset.UtcNow))];
                case RestoreExcludedItemsCommand restore:
                    ThrowIfFailing();
                    bed.Restores.Add(restore);
                    bed.Removed.RemoveAll(restore.ItemIds.Contains);
                    return new RestoreExcludedItemsResult(restore.ItemIds.Count, 0, 0);
                case ForgetExcludedItemsCommand forget:
                    ThrowIfFailing();
                    bed.Forgets.Add(forget);
                    bed.Removed.RemoveAll(forget.ItemIds.Contains);
                    return forget.ItemIds.Count;
                default:
                    throw new NotSupportedException(request.GetType().Name);
            }
        }

        private void ThrowIfFailing()
        {
            if (bed.Failure is { } failure)
            {
                throw failure;
            }
        }
    }
}
