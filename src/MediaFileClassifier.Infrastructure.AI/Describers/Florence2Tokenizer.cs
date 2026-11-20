using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MediaFileClassifier.Infrastructure.AI.Describers;

/// <summary>
/// Florence-2's tokenizer (BART's byte-level BPE) read from its <c>tokenizer.json</c>: the GPT-2 split into words,
/// bytes mapped to printable characters, merges by rank, and <c>&lt;s&gt;</c> … <c>&lt;/s&gt;</c> around the text.
/// Token IDs equal the Hugging Face <c>tokenizers</c> library's for the task prompts. Every added token (the special
/// tokens and <c>&lt;loc_N&gt;</c>) decodes as its literal text, or is skipped.
/// </summary>
internal sealed partial class Florence2Tokenizer
{
    /// <summary>
    /// The start of a sequence (<c>&lt;s&gt;</c>).
    /// </summary>
    public const int BeginId = 0;

    /// <summary>
    /// The end of a sequence (<c>&lt;/s&gt;</c>), also the decoder's start token.
    /// </summary>
    public const int EndId = 2;

    private static readonly char[] ByteToChar = BuildByteToChar();
    private static readonly Dictionary<char, byte> CharToByte = ByteToChar.Select((c, b) => (c, b)).ToDictionary(pair => pair.c, pair => (byte)pair.b);

    private readonly Dictionary<string, int> vocabulary;
    private readonly Dictionary<(string Left, string Right), int> mergeRanks;
    private readonly Dictionary<int, string> tokens;
    private readonly HashSet<int> addedIds;

    private Florence2Tokenizer(Dictionary<string, int> vocabulary, Dictionary<(string Left, string Right), int> mergeRanks, Dictionary<int, string> added)
    {
        this.vocabulary = vocabulary;
        this.mergeRanks = mergeRanks;
        tokens = vocabulary.ToDictionary(pair => pair.Value, pair => pair.Key);
        foreach (var (id, content) in added)
        {
            tokens[id] = content;
        }

        addedIds = [.. added.Keys];
    }

    /// <summary>
    /// Loads the tokenizer from <c>tokenizer.json</c>.
    /// </summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The tokenizer.</returns>
    /// <exception cref="InvalidDataException">The file isn't a byte-level BPE tokenizer.</exception>
    public static Florence2Tokenizer Load(string path)
    {
        using var stream = File.OpenRead(path);
        return Load(stream);
    }

    /// <summary>
    /// Loads the tokenizer from the content of <c>tokenizer.json</c>.
    /// </summary>
    /// <param name="stream">The content.</param>
    /// <returns>The tokenizer.</returns>
    /// <exception cref="InvalidDataException">The content isn't a byte-level BPE tokenizer.</exception>
    public static Florence2Tokenizer Load(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (!root.TryGetProperty("model", out var model) || !model.TryGetProperty("vocab", out var vocab) || !model.TryGetProperty("merges", out var merges))
        {
            throw new InvalidDataException("The tokenizer has no BPE vocabulary and merges.");
        }

        var vocabulary = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in vocab.EnumerateObject())
        {
            vocabulary[entry.Name] = entry.Value.GetInt32();
        }

        var ranks = new Dictionary<(string Left, string Right), int>();
        var rank = 0;
        foreach (var merge in merges.EnumerateArray())
        {
            // tokenizers writes a merge as "a b", or as ["a", "b"] in newer files.
            var pair = merge.ValueKind == JsonValueKind.Array
                ? (merge[0].GetString()!, merge[1].GetString()!)
                : Split(merge.GetString()!);
            ranks.TryAdd(pair, rank++);
        }

        var added = new Dictionary<int, string>();
        if (root.TryGetProperty("added_tokens", out var addedTokens))
        {
            foreach (var token in addedTokens.EnumerateArray())
            {
                added[token.GetProperty("id").GetInt32()] = token.GetProperty("content").GetString()!;
            }
        }

        return new Florence2Tokenizer(vocabulary, ranks, added);
    }

    /// <summary>
    /// Encodes a prompt as the model expects it: <c>&lt;s&gt;</c>, the BPE tokens, <c>&lt;/s&gt;</c>.
    /// </summary>
    /// <param name="text">The prompt.</param>
    /// <returns>The token IDs.</returns>
    public long[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var ids = new List<long> { BeginId };
        foreach (Match word in WordPattern().Matches(text))
        {
            var bytes = Encoding.UTF8.GetBytes(word.Value);
            var symbol = new string(bytes.Select(b => ByteToChar[b]).ToArray());
            foreach (var piece in Merge(symbol))
            {
                ids.Add(vocabulary.TryGetValue(piece, out var id)
                    ? id
                    : throw new InvalidDataException($"The tokenizer has no token for '{piece}'."));
            }
        }

        ids.Add(EndId);
        return [.. ids];
    }

    /// <summary>
    /// Decodes token IDs to text.
    /// </summary>
    /// <param name="ids">The IDs.</param>
    /// <param name="skipAdded"><c>true</c> to leave out the added tokens (<c>&lt;s&gt;</c>, <c>&lt;loc_N&gt;</c> and the like).</param>
    /// <returns>The text.</returns>
    public string Decode(IEnumerable<int> ids, bool skipAdded)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var text = new StringBuilder();
        var bytes = new List<byte>();
        foreach (var id in ids)
        {
            if (!tokens.TryGetValue(id, out var token))
            {
                continue;
            }

            if (addedIds.Contains(id))
            {
                Flush();
                if (!skipAdded)
                {
                    text.Append(token);
                }

                continue;
            }

            foreach (var c in token)
            {
                if (CharToByte.TryGetValue(c, out var b))
                {
                    bytes.Add(b);
                }
            }
        }

        Flush();
        return text.ToString();

        void Flush()
        {
            if (bytes.Count > 0)
            {
                text.Append(Encoding.UTF8.GetString([.. bytes]));
                bytes.Clear();
            }
        }
    }

    private static (string Left, string Right) Split(string merge)
    {
        var space = merge.IndexOf(' ', StringComparison.Ordinal);
        return space < 0
            ? throw new InvalidDataException($"The merge '{merge}' has no space.")
            : (merge[..space], merge[(space + 1)..]);
    }

    /// <summary>
    /// GPT-2's map of the 256 bytes to printable characters: printable Latin-1 bytes map to themselves, the others to
    /// U+0100 onwards, in byte order.
    /// </summary>
    private static char[] BuildByteToChar()
    {
        var map = new char[256];
        var next = 256;
        for (var b = 0; b < 256; b++)
        {
            var printable = b is >= '!' and <= '~' or >= 0xA1 and <= 0xAC or >= 0xAE and <= 0xFF;
            map[b] = printable ? (char)b : (char)next++;
        }

        return map;
    }

    [GeneratedRegex(@"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();

    private List<string> Merge(string word)
    {
        var parts = word.Select(c => c.ToString()).ToList();
        while (parts.Count > 1)
        {
            var best = -1;
            var bestRank = int.MaxValue;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                if (mergeRanks.TryGetValue((parts[i], parts[i + 1]), out var rank) && rank < bestRank)
                {
                    bestRank = rank;
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            parts[best] += parts[best + 1];
            parts.RemoveAt(best + 1);
        }

        return parts;
    }
}
