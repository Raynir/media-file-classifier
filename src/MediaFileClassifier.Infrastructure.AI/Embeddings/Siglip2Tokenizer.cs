using Microsoft.ML.Tokenizers;

namespace MediaFileClassifier.Infrastructure.AI.Embeddings;

/// <summary>
/// The SigLIP 2 text input (D092): the text lower-cased, Gemma SentencePiece pieces, <c>&lt;/s&gt;</c> appended,
/// padded with 0 to 64 tokens. Token IDs equal the Hugging Face processor's (E06-S03-T03).
/// </summary>
internal sealed class Siglip2Tokenizer
{
    private const long PadId = 0;
    private readonly SentencePieceTokenizer tokenizer;

    private Siglip2Tokenizer(SentencePieceTokenizer tokenizer)
    {
        this.tokenizer = tokenizer;
    }

    /// <summary>
    /// Loads the tokenizer from its SentencePiece model file.
    /// </summary>
    /// <param name="path">Path of <c>tokenizer.model</c>.</param>
    /// <returns>The tokenizer.</returns>
    public static Siglip2Tokenizer Load(string path)
    {
        using var stream = File.OpenRead(path);
        return new Siglip2Tokenizer(SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false));
    }

    /// <summary>
    /// Normalizes a text the way the model was trained: lower case, invariant culture.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The normalized text.</returns>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.ToLowerInvariant();
    }

    /// <summary>
    /// Writes the token IDs of one text, truncated to 63 pieces plus <c>&lt;/s&gt;</c> like the reference.
    /// </summary>
    /// <param name="text">The text, not yet lower-cased.</param>
    /// <param name="destination"><see cref="Siglip2Model.SequenceLength"/> IDs.</param>
    public void Encode(string text, Span<long> destination)
    {
        if (destination.Length < Siglip2Model.SequenceLength)
        {
            throw new ArgumentException($"The destination needs {Siglip2Model.SequenceLength} IDs.", nameof(destination));
        }

        var pieces = tokenizer.EncodeToIds(Normalize(text), addBeginningOfSentence: false, addEndOfSentence: false);
        var count = Math.Min(pieces.Count, Siglip2Model.SequenceLength - 1);
        for (var i = 0; i < count; i++)
        {
            destination[i] = pieces[i];
        }

        destination[count] = tokenizer.EndOfSentenceId;
        destination[(count + 1)..Siglip2Model.SequenceLength].Fill(PadId);
    }
}
