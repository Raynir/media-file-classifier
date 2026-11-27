using MediaFileClassifier.Ui.Browser;
using MediaFileClassifier.Ui.Tests.Browser;
using MediaFileClassifier.Ui.Tests.Shell;
using MediaFileClassifier.Ui.ViewModels.Duplicates;
using MediatR;

namespace MediaFileClassifier.Ui.Tests.Duplicates;

/// <summary>
/// Builds the comparison view model for the Duplicates tests, over fakes: no thumbnails exist, so previews stay empty.
/// </summary>
internal static class DuplicateTestBed
{
    public static DuplicateComparisonViewModel Comparison(ISender sender, FakeSettingsService? settings = null, FakeBrowserSession? session = null)
    {
        session ??= new FakeBrowserSession();
        return new DuplicateComparisonViewModel(
            sender,
            new ThumbnailLoader(session, new FakeThumbnailCache(), new ThumbnailBitmapCache()),
            new KeeperRuleSettings(settings ?? new FakeSettingsService(), session),
            TestLocalizer.EnUs());
    }
}
