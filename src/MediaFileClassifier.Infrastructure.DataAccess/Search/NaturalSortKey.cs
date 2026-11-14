using System.Globalization;
using System.Text;

namespace MediaFileClassifier.Infrastructure.DataAccess.Search;

/// <summary>
/// The name sort key (E10-S04-T01): the file name of a path, upper-cased in every alphabet, with each run of ASCII digits
/// written as its length and then the digits, so binary comparison puts numbers in numeric order ("IMG_2" before
/// "IMG_10"). <c>search_sort.name_key</c> holds it, written by the triggers through the <c>natural_key</c> SQL function
/// every library connection has.
/// </summary>
internal static class NaturalSortKey
{
    /// <summary>
    /// The longest digit run that keeps its numeric order; longer runs are cut to this many digits.
    /// </summary>
    public const int MaxDigits = 999;

    /// <summary>
    /// Makes the key of a path: of its file name, the part after the last <c>/</c>.
    /// </summary>
    /// <param name="path">The path in the folder, with <c>/</c> separators.</param>
    /// <returns>The key, or <c>null</c> for a <c>null</c> path.</returns>
    public static string? Create(string? path)
    {
        if (path is null)
        {
            return null;
        }

        var name = path.AsSpan(path.LastIndexOf('/') + 1);
        var key = new StringBuilder(name.Length + 8);
        var i = 0;
        while (i < name.Length)
        {
            if (!char.IsAsciiDigit(name[i]))
            {
                var start = i;
                while (i < name.Length && !char.IsAsciiDigit(name[i]))
                {
                    i++;
                }

                key.Append(name[start..i].ToString().ToUpperInvariant());
                continue;
            }

            // Leading zeros don't change the number: "007" and "7" get the same key, and the document ID orders them.
            while (i < name.Length - 1 && name[i] == '0' && char.IsAsciiDigit(name[i + 1]))
            {
                i++;
            }

            var digits = i;
            while (i < name.Length && char.IsAsciiDigit(name[i]))
            {
                i++;
            }

            var length = Math.Min(i - digits, MaxDigits);
            key.Append(length.ToString("D3", CultureInfo.InvariantCulture)).Append(name.Slice(digits, length));
        }

        return key.ToString();
    }
}
