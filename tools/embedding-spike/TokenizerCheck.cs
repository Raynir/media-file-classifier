#:package Microsoft.ML.Tokenizers

// Checks that Microsoft.ML.Tokenizers gives the same token IDs as the Python reference tokenizers (E06-S03-T03).
// Input: models/token-cases.json from tools/embedding-spike/export_token_cases.py. Run from the repository root:
//   dotnet run tools/embedding-spike/TokenizerCheck.cs
using System.Text.Json;
using Microsoft.ML.Tokenizers;

var cases = JsonDocument.Parse(File.ReadAllText("models/token-cases.json")).RootElement;
var failed = 0;
foreach (var family in cases.EnumerateObject())
{
    SentencePieceTokenizer tokenizer;
    try
    {
        using var stream = File.OpenRead(family.Value.GetProperty("model").GetString()!);
        tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false);
    }
    catch (Exception exception)
    {
        Console.WriteLine($"{family.Name}: the model doesn't load ({exception.GetType().Name}: {exception.Message})");
        failed++;
        continue;
    }

    Func<string, long[]> encode = family.Name switch
    {
        // SigLIP 2 (Gemma): lower-cased by the caller, </s>=1 appended, padded with <pad>=0 to 64.
        "siglip2" => text => [.. tokenizer.EncodeToIds(text, false, false).Select(id => (long)id).Append(1).Concat(Enumerable.Repeat(0L, 64)).Take(64)],

        // XLM-R and XLM-V (fairseq IDs): <s>=0, </s>=2, <unk>=3, SentencePiece IDs shifted by one.
        "xlmr" or "xlmv" => text => [0, .. tokenizer.EncodeToIds(text, false, false).Select(id => id == 0 ? 3L : id + 1L), 2],

        // mT5: SentencePiece IDs as they are, </s>=1 appended.
        "mt5" => text => [.. tokenizer.EncodeToIds(text, false, false).Select(id => (long)id), 1],
        _ => throw new NotSupportedException(family.Name),
    };

    var total = 0;
    var mismatches = 0;
    foreach (var item in family.Value.GetProperty("cases").EnumerateArray())
    {
        var text = item.GetProperty("text").GetString()!;
        var expected = item.GetProperty("ids").EnumerateArray().Select(id => id.GetInt64()).ToArray();
        var actual = encode(text);
        total++;
        if (!expected.SequenceEqual(actual))
        {
            mismatches++;
            Console.WriteLine($"  {family.Name} mismatch: {text}\n    expected {string.Join(' ', expected)}\n    actual   {string.Join(' ', actual)}");
        }
    }

    Console.WriteLine($"{family.Name}: {total - mismatches}/{total} queries match");
    failed += mismatches;
}

return failed == 0 ? 0 : 1;
