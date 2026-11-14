using MediaFileClassifier.Infrastructure.AI.Embeddings;

namespace MediaFileClassifier.Infrastructure.Tests.AI.Embeddings;

/// <summary>
/// Checks the SigLIP 2 tokenizer against the Hugging Face reference token IDs (E06-S03-T03). Needs
/// <c>tokenizer.model</c>, which isn't committed, so it skips where the model isn't installed.
/// </summary>
public sealed class Siglip2TokenizerTests
{
    [Fact]
    public void Tokenizer_AllEvaluationQueries_MatchReferenceIds()
    {
        Siglip2TestModel.RequireDirectory();
        var tokenizer = Siglip2Tokenizer.Load(Siglip2TestModel.TokenizerPath!);
        var ids = new long[Siglip2Model.SequenceLength];

        var mismatches = new List<string>();
        foreach (var query in Siglip2TestModel.Reference.Tokens)
        {
            tokenizer.Encode(query.Text, ids);
            if (!ids.SequenceEqual(query.Ids))
            {
                mismatches.Add(query.Id);
            }
        }

        Assert.Equal(215, Siglip2TestModel.Reference.Tokens.Count);
        Assert.Empty(mismatches);
        Assert.Contains(Siglip2TestModel.Reference.Tokens, query => query.Id.StartsWith("ru-", StringComparison.Ordinal));
    }

    [Fact]
    public void Tokenizer_LongText_KeepsEndOfSentenceLast()
    {
        Siglip2TestModel.RequireDirectory();
        var tokenizer = Siglip2Tokenizer.Load(Siglip2TestModel.TokenizerPath!);
        var ids = new long[Siglip2Model.SequenceLength];

        tokenizer.Encode(string.Join(' ', Enumerable.Repeat("кошка на подоконнике", 40)), ids);

        Assert.Equal(1, ids[^1]);
        Assert.DoesNotContain(0L, ids);
    }
}
