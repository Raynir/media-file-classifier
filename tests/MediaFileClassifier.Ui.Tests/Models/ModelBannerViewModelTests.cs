using System.Globalization;
using System.Resources;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.Ui.ViewModels.Models;
using MediaFileClassifier.UseCases.Models.DownloadModel;
using MediaFileClassifier.UseCases.Models.GetModelOffer;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.UseCases.Models.ImportModel;

namespace MediaFileClassifier.Ui.Tests.Models;

/// <summary>
/// Tests for <see cref="ModelBannerViewModel"/> (E06-S12-T04): the banner shows while the embedding model is missing,
/// downloads only with consent, and shows failures with Retry.
/// </summary>
public sealed class ModelBannerViewModelTests
{
    private readonly ModelSender sender = new();
    private readonly FakeDialogs dialogs = new();

    [Fact]
    public async Task Refresh_ModelMissing_ShowsDownloadAndImport()
    {
        var banner = Create();

        await banner.RefreshAsync();

        Assert.Equal(ModelBannerState.Missing, banner.State);
        Assert.True(banner.IsVisible);
        Assert.True(banner.IsMissing);
        Assert.StartsWith("Search needs an AI model", banner.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ModelInstalled_HidesTheBanner()
    {
        sender.Installed = true;
        var banner = Create();

        await banner.RefreshAsync();

        Assert.False(banner.IsVisible);
    }

    [Fact]
    public async Task Download_Agreed_DownloadsAndHides()
    {
        var banner = Create();
        await banner.RefreshAsync();
        dialogs.Agree = true;
        sender.OnDownload = () => sender.Installed = true;

        await banner.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.Equal(1, sender.Downloads);
        Assert.Equal(ModelBannerState.Hidden, banner.State);
    }

    [Fact]
    public async Task Download_Declined_DownloadsNothing()
    {
        var banner = Create();
        await banner.RefreshAsync();

        await banner.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.Equal(0, sender.Downloads);
        Assert.Equal(ModelBannerState.Missing, banner.State);
    }

    [Theory]
    [InlineData(ModelDownloadFailure.HashMismatch, "damaged")]
    [InlineData(ModelDownloadFailure.DiskFull, "disk space")]
    [InlineData(ModelDownloadFailure.Network, "connection")]
    public async Task Download_Fails_ShowsTheErrorAndRetryDownloadsWithoutAskingAgain(ModelDownloadFailure failure, string text)
    {
        var banner = Create();
        await banner.RefreshAsync();
        dialogs.Agree = true;
        sender.OnDownload = () => throw new ModelDownloadException(failure, "detail");

        await banner.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(ModelBannerState.Failed, banner.State);
        Assert.Contains(text, banner.Message, StringComparison.Ordinal);
        Assert.Equal("detail", banner.ErrorDetail);

        sender.OnDownload = () => sender.Installed = true;
        await banner.RetryCommand.ExecuteAsync(null);

        Assert.Equal(1, dialogs.Confirmations);
        Assert.Equal(2, sender.Downloads);
        Assert.Equal(ModelBannerState.Hidden, banner.State);
    }

    [Fact]
    public async Task Download_Cancelled_ShowsTheBannerAgain()
    {
        var banner = Create();
        await banner.RefreshAsync();
        dialogs.Agree = true;
        sender.OnDownload = () =>
        {
            Assert.Equal(ModelBannerState.Downloading, banner.State);
            banner.CancelDownloadCommand.Execute(null);
            throw new OperationCanceledException();
        };

        await banner.DownloadCommand.ExecuteAsync(null);

        Assert.Equal(ModelBannerState.Missing, banner.State);
    }

    [Fact]
    public async Task Import_SendsThePickedFiles_AndAFailureShowsTheError()
    {
        var banner = Create();
        await banner.RefreshAsync();
        dialogs.Picked = [@"C:\models\vision_model.onnx"];
        sender.OnImport = () => throw new ModelStoreException("wrong hash");

        await banner.ImportCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\models\vision_model.onnx"], sender.ImportedPaths);
        Assert.Equal(ModelBannerState.Failed, banner.State);
        Assert.Contains("imported", banner.Message, StringComparison.Ordinal);
        Assert.Equal("wrong hash", banner.ErrorDetail);
    }

    [Fact]
    public async Task Import_PickerCancelled_ImportsNothing()
    {
        var banner = Create();
        await banner.RefreshAsync();

        await banner.ImportCommand.ExecuteAsync(null);

        Assert.Null(sender.ImportedPaths);
        Assert.Equal(ModelBannerState.Missing, banner.State);
    }

    private ModelBannerViewModel Create() =>
        new(sender, dialogs, new Localizer(new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
        });

    private sealed class FakeDialogs : IModelDialogs
    {
        public bool Agree { get; set; }

        public int Confirmations { get; private set; }

        public IReadOnlyList<string> Picked { get; set; } = [];

        public Task<bool> ConfirmDownloadAsync(ModelOffer offer)
        {
            Confirmations++;
            return Task.FromResult(Agree);
        }

        public Task<IReadOnlyList<string>> PickModelFilesAsync() => Task.FromResult(Picked);
    }

    /// <summary>
    /// Answers the model use cases.
    /// </summary>
    private sealed class ModelSender : FakeSender
    {
        public bool Installed { get; set; }

        public int Downloads { get; private set; }

        public IReadOnlyList<string>? ImportedPaths { get; private set; }

        public Action OnDownload { get; set; } = () => { };

        public Action OnImport { get; set; } = () => { };

        protected override object? Handle(object request)
        {
            object? response;
            switch (request)
            {
                case GetModelOfferQuery:
                    response = new ModelOffer("SigLIP 2", 1_505_702_732, "Apache-2.0", new Uri("https://example.com/l"), new Uri("https://example.com/m"), Installed);
                    break;
                case DownloadModelCommand:
                    Downloads++;
                    OnDownload();
                    response = null;
                    break;
                case ImportModelCommand import:
                    ImportedPaths = import.Paths;
                    OnImport();
                    response = null;
                    break;
                default:
                    throw new NotSupportedException(request.GetType().Name);
            }

            return response;
        }
    }
}
