using MediaFileClassifier.Ui.Models;
using MediaFileClassifier.UseCases.Models.GetModelOffer;

namespace MediaFileClassifier.Ui.Tests.Models;

/// <summary>
/// Model dialogs that answer with what the test sets: <see cref="Agree"/> for the download consent and
/// <see cref="Picked"/> for the file picker. It keeps the last offer shown and counts the confirmations.
/// </summary>
internal sealed class FakeModelDialogs : IModelDialogs
{
    public bool Agree { get; set; }

    public IReadOnlyList<string> Picked { get; set; } = [];

    public ModelOffer? Offer { get; private set; }

    public int Confirmations { get; private set; }

    public Task<bool> ConfirmDownloadAsync(ModelOffer offer)
    {
        Offer = offer;
        Confirmations++;
        return Task.FromResult(Agree);
    }

    public Task<IReadOnlyList<string>> PickModelFilesAsync() => Task.FromResult(Picked);
}
