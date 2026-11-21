using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.ViewModels.Libraries;
using MediaFileClassifier.UseCases.Libraries.DeleteLibrary;

namespace MediaFileClassifier.Ui.Tests.Libraries;

/// <summary>
/// Tests for the delete-library confirmation (E02-S07-T02): one click can't delete a library; the exact name must be
/// typed first.
/// </summary>
public sealed class DeleteLibraryViewModelTests
{
    private const string Family = @"D:\Family.mfc";

    [Fact]
    public async Task Load_ShowsTheItemCount_AndThatTheOpenLibraryIsClosedFirst()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, "Family", 12, IsOpen: true, Error: null));

        await bed.ViewModel.LoadAsync(Family, "Family");

        Assert.Equal("Delete the library “Family”?", bed.ViewModel.Heading);
        Assert.Equal("This deletes the library’s index of 12 items: its database, thumbnails and backups.", bed.ViewModel.Summary);
        Assert.Equal("Type Family to confirm.", bed.ViewModel.Prompt);
        Assert.True(bed.ViewModel.IsOpen);
        Assert.True(bed.ViewModel.CanConfirm);
    }

    [Fact]
    public async Task Delete_IsDisabled_UntilTheExactNameIsTyped()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, "Family", 3, IsOpen: false, Error: null));
        await bed.ViewModel.LoadAsync(Family, "Family");

        Assert.False(bed.ViewModel.DeleteCommand.CanExecute(null));
        bed.ViewModel.TypedName = "family";
        Assert.False(bed.ViewModel.DeleteCommand.CanExecute(null));
        bed.ViewModel.TypedName = "Famil";
        Assert.False(bed.ViewModel.DeleteCommand.CanExecute(null));
        bed.ViewModel.TypedName = "  Family ";
        Assert.True(bed.ViewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_SendsTheCommand_AndCloses()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, "Family", 3, IsOpen: false, Error: null));
        await bed.ViewModel.LoadAsync(Family, "Family");
        var closed = false;
        bed.ViewModel.CloseRequested += (_, _) => closed = true;
        bed.ViewModel.TypedName = "Family";

        await bed.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(Family, Assert.Single(bed.Deletes).LibraryDirectory);
        Assert.True(closed);
        Assert.True(bed.ViewModel.Result!.Deleted);
    }

    [Fact]
    public async Task Load_LibraryOpenElsewhere_ShowsWhy_AndCantBeDeleted()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, null, 0, IsOpen: false, LibraryOpenError.InUse));

        await bed.ViewModel.LoadAsync(Family, "Family");
        bed.ViewModel.TypedName = "Family";

        Assert.Equal("Family", bed.ViewModel.Name);
        Assert.Equal("This library is open in another window of the app.", bed.ViewModel.Error);
        Assert.False(bed.ViewModel.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task Delete_FailsAfterTheOpenLibraryWasClosed_SaysItWasClosed_AndStaysOpen()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, "Family", 3, IsOpen: true, Error: null))
        {
            DeleteResult = new DeleteLibraryResult(LibraryOpenError.NotWritable, FolderRemoved: false, KeptEntries: []),
        };
        await bed.ViewModel.LoadAsync(Family, "Family");
        var closed = false;
        bed.ViewModel.CloseRequested += (_, _) => closed = true;
        bed.ViewModel.TypedName = "Family";

        await bed.ViewModel.DeleteCommand.ExecuteAsync(null);

        Assert.False(closed);
        Assert.Null(bed.ViewModel.Result);
        Assert.StartsWith("The library was closed, but it couldn't be deleted.", bed.ViewModel.Error);
        Assert.False(bed.ViewModel.IsOpen);
    }

    [Fact]
    public async Task Load_ADeleteCutShort_SaysDeletingFinishesIt()
    {
        var bed = new Bed(new LibraryDeletionInfo(Family, "Family", 0, IsOpen: false, Error: null) { IsPartial = true });

        await bed.ViewModel.LoadAsync(Family, "Family");

        Assert.StartsWith("An earlier delete of this library was cut short", bed.ViewModel.Summary);
    }

    private sealed class Bed : FakeSender
    {
        private readonly LibraryDeletionInfo info;

        public Bed(LibraryDeletionInfo info)
        {
            this.info = info;
            ViewModel = new DeleteLibraryViewModel(this, TestLocalizer.EnUs());
        }

        public DeleteLibraryViewModel ViewModel { get; }

        public DeleteLibraryResult DeleteResult { get; init; } = new(null, FolderRemoved: true, KeptEntries: []);

        public List<DeleteLibraryCommand> Deletes { get; } = [];

        protected override object? Handle(object request)
        {
            switch (request)
            {
                case GetLibraryDeletionInfoQuery:
                    return info;
                case DeleteLibraryCommand delete:
                    Deletes.Add(delete);
                    return DeleteResult;
                default:
                    return base.Handle(request);
            }
        }
    }
}
