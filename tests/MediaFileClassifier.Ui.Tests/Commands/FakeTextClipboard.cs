using MediaFileClassifier.Ui.Platform;

namespace MediaFileClassifier.Ui.Tests.Commands;

/// <summary>
/// An <see cref="ITextClipboard"/> that keeps the last text.
/// </summary>
internal sealed class FakeTextClipboard : ITextClipboard
{
    public string? Text { get; private set; }

    public Task SetTextAsync(string text)
    {
        Text = text;
        return Task.CompletedTask;
    }
}
