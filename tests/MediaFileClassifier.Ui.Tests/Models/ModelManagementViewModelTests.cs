using System.Globalization;
using System.Resources;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Testing.UseCases;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.Ui.ViewModels.Models;
using MediaFileClassifier.UseCases.Models.DeleteModel;
using MediaFileClassifier.UseCases.Models.DownloadModel;
using MediaFileClassifier.UseCases.Models.GetModelOffer;
using MediaFileClassifier.UseCases.Models.GetModels;
using MediaFileClassifier.UseCases.Models.GetModelUsage;
using MediaFileClassifier.UseCases.Models.ImportModel;
using MediaFileClassifier.UseCases.Models.VerifyModel;

namespace MediaFileClassifier.Ui.Tests.Models;

/// <summary>
/// Tests for <see cref="ModelManagementViewModel"/> and <see cref="ModelRowViewModel"/> (E17-S08-T01): one row per
/// tier, download of the row's tier with consent, import, verification and deletion that asks first.
/// </summary>
public sealed class ModelManagementViewModelTests
{
    private readonly ModelSender sender = new();
    private readonly FakeDialogs dialogs = new();

    [Fact]
    public async Task Refresh_ShowsOneRowPerTier_WithItsState()
    {
        sender.Entries = [Entry("standard", installed: true), Entry("compact", installed: false)];
        var page = Create();

        await page.RefreshAsync();

        Assert.Equal(2, page.Rows.Count);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
        Assert.True(page.Rows[0].IsInstalledIdle);
        Assert.Equal("SigLIP 2 — Standard", page.Rows[0].Name);
        Assert.Contains("Apache-2.0", page.Rows[0].Details, StringComparison.Ordinal);
        Assert.Equal(ModelRowState.NotInstalled, page.Rows[1].State);
        Assert.True(page.Rows[1].CanDownload);
    }

    [Fact]
    public async Task Refresh_KeepsTheRowObjects_AndFollowsTheStore()
    {
        sender.Entries = [Entry("standard", installed: false)];
        var page = Create();
        await page.RefreshAsync();
        var row = page.Rows[0];

        sender.Entries = [Entry("standard", installed: true)];
        await page.RefreshAsync();

        Assert.Same(row, page.Rows[0]);
        Assert.Equal(ModelRowState.Installed, row.State);
    }

    [Fact]
    public async Task Download_Agreed_DownloadsTheRowsTier_AndShowsItInstalled()
    {
        sender.Entries = [Entry("compact", installed: false)];
        var page = Create();
        await page.RefreshAsync();
        dialogs.Agree = true;

        await page.Rows[0].DownloadCommand.ExecuteAsync(null);

        Assert.Equal("compact", sender.DownloadedTier);
        Assert.Equal("SigLIP 2 — Compact", dialogs.Offer!.DisplayName);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
        Assert.Equal("Installed and verified.", page.Rows[0].Status);
    }

    [Fact]
    public async Task Download_Declined_DownloadsNothing()
    {
        sender.Entries = [Entry("compact", installed: false)];
        var page = Create();
        await page.RefreshAsync();

        await page.Rows[0].DownloadCommand.ExecuteAsync(null);

        Assert.Null(sender.DownloadedTier);
        Assert.Equal(ModelRowState.NotInstalled, page.Rows[0].State);
    }

    [Fact]
    public async Task Download_HashMismatch_ShowsTheError()
    {
        sender.Entries = [Entry("standard", installed: false)];
        sender.OnDownload = () => throw new ModelDownloadException(ModelDownloadFailure.HashMismatch, "detail");
        var page = Create();
        await page.RefreshAsync();
        dialogs.Agree = true;

        await page.Rows[0].DownloadCommand.ExecuteAsync(null);

        Assert.True(page.Rows[0].IsError);
        Assert.Contains("damaged", page.Rows[0].Status, StringComparison.Ordinal);
        Assert.Equal("detail", page.Rows[0].ErrorDetail);
        Assert.True(page.Rows[0].CanDownload);
    }

    [Fact]
    public async Task Import_SendsThePickedFilesForTheRowsPurpose()
    {
        sender.Entries = [Entry("standard", installed: false)];
        var page = Create();
        await page.RefreshAsync();
        dialogs.Picked = [@"C:\m\vision_model.onnx", @"C:\m\text_model.onnx"];

        await page.Rows[0].ImportCommand.ExecuteAsync(null);

        Assert.Equal(dialogs.Picked, sender.ImportedPaths);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
    }

    [Theory]
    [InlineData(false, "Verified: the files are intact.")]
    [InlineData(true, "Verification failed")]
    public async Task Verify_ShowsTheResult(bool damaged, string status)
    {
        sender.Entries = [Entry("standard", installed: true)];
        sender.Problems = damaged ? ["vision_model.onnx: hash mismatch"] : [];
        var page = Create();
        await page.RefreshAsync();

        await page.Rows[0].VerifyCommand.ExecuteAsync(null);

        Assert.StartsWith(status, page.Rows[0].Status, StringComparison.Ordinal);
        Assert.Equal(damaged, page.Rows[0].IsError);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
        Assert.Equal(damaged ? "vision_model.onnx: hash mismatch" : null, page.Rows[0].ErrorDetail);
    }

    [Fact]
    public async Task Delete_AsksFirst_NamingTheLibraries_AndOnlyDeletesWhenConfirmed()
    {
        sender.Entries = [Entry("standard", installed: true)];
        sender.Using = [@"C:\Libraries\Photos.mfclib"];
        var page = Create();
        await page.RefreshAsync();
        var row = page.Rows[0];

        await row.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(ModelRowState.ConfirmingDelete, row.State);
        Assert.Contains("Photos.mfclib", row.DeleteQuestion, StringComparison.Ordinal);
        Assert.Null(sender.Deleted);

        row.CancelDeleteCommand.Execute(null);
        Assert.Equal(ModelRowState.Installed, row.State);
        Assert.Null(sender.Deleted);

        await row.DeleteCommand.ExecuteAsync(null);
        await row.ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.Equal(new DeleteModelCommand("siglip2", "1.0", "standard", Confirmed: true), sender.Deleted);
        Assert.Equal(ModelRowState.NotInstalled, row.State);
    }

    [Fact]
    public async Task Delete_InUse_ShowsTheErrorAndKeepsTheModel()
    {
        sender.Entries = [Entry("standard", installed: true)];
        sender.OnDelete = () => throw new ModelStoreException("in use");
        var page = Create();
        await page.RefreshAsync();

        await page.Rows[0].DeleteCommand.ExecuteAsync(null);
        await page.Rows[0].ConfirmDeleteCommand.ExecuteAsync(null);

        Assert.True(page.Rows[0].IsError);
        Assert.Equal(ModelRowState.Installed, page.Rows[0].State);
    }

    private static ModelListEntry Entry(string tier, bool installed) => new(
        "siglip2",
        "1.0",
        tier,
        "SigLIP 2",
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tier),
        "fp32",
        1_505_702_732,
        "Apache-2.0",
        new Uri("https://example.com/license"),
        new Uri("https://example.com/siglip2"),
        ModelPurpose.Embedding,
        IsDefaultTier: tier == "standard",
        installed ? DateTimeOffset.UnixEpoch : null);

    private ModelManagementViewModel Create() =>
        new(sender, dialogs, new Localizer(new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly))
        {
            Culture = CultureInfo.GetCultureInfo("en-US"),
        });

    private sealed class FakeDialogs : IModelDialogs
    {
        public bool Agree { get; set; }

        public ModelOffer? Offer { get; private set; }

        public IReadOnlyList<string> Picked { get; set; } = [];

        public Task<bool> ConfirmDownloadAsync(ModelOffer offer)
        {
            Offer = offer;
            return Task.FromResult(Agree);
        }

        public Task<IReadOnlyList<string>> PickModelFilesAsync() => Task.FromResult(Picked);
    }

    /// <summary>
    /// Answers the model use cases.
    /// </summary>
    private sealed class ModelSender : FakeSender
    {
        public IReadOnlyList<ModelListEntry> Entries { get; set; } = [];

        public IReadOnlyList<string> Problems { get; set; } = [];

        public IReadOnlyList<string> Using { get; set; } = [];

        public string? DownloadedTier { get; private set; }

        public IReadOnlyList<string>? ImportedPaths { get; private set; }

        public DeleteModelCommand? Deleted { get; private set; }

        public Action OnDownload { get; set; } = () => { };

        public Action OnDelete { get; set; } = () => { };

        protected override object? Handle(object request)
        {
            object? response;
            switch (request)
            {
                case GetModelsQuery:
                    response = Entries;
                    break;
                case DownloadModelCommand download:
                    OnDownload();
                    DownloadedTier = download.TierId;
                    response = null;
                    break;
                case ImportModelCommand import:
                    ImportedPaths = import.Paths;
                    response = null;
                    break;
                case VerifyModelCommand:
                    response = Problems;
                    break;
                case GetModelUsageQuery:
                    response = Using;
                    break;
                case DeleteModelCommand delete:
                    OnDelete();
                    Deleted = delete;
                    response = new ModelDeletionResult(Deleted: true, Using);
                    break;
                default:
                    throw new NotSupportedException(request.GetType().Name);
            }

            return response;
        }
    }
}
