namespace MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

/// <summary>
/// Why a capability is unavailable, as a code and arguments rather than text (E13-S08). The UI shows the resource
/// string <c>CapabilityReason_&lt;Code&gt;</c>, a composite format filled with <see cref="Arguments"/>.
/// Create reasons through <see cref="CapabilityReasons"/>, which lists every code.
/// </summary>
/// <param name="Code">Reason code, one of the <see cref="CapabilityReasons"/> constants.</param>
/// <param name="Arguments">Values for the format placeholders, such as a file path.</param>
public sealed record CapabilityReason(string Code, IReadOnlyList<string> Arguments)
{
    /// <inheritdoc />
    public bool Equals(CapabilityReason? other)
        => other is not null
            && string.Equals(Code, other.Code, StringComparison.Ordinal)
            && Arguments.SequenceEqual(other.Arguments, StringComparer.Ordinal);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = StringComparer.Ordinal.GetHashCode(Code);
        foreach (var argument in Arguments)
        {
            hash = HashCode.Combine(hash, StringComparer.Ordinal.GetHashCode(argument));
        }

        return hash;
    }

    /// <summary>
    /// Formats the reason for the log, e.g. <c>FfprobeNotFound(C:\ffmpeg\bin)</c>.
    /// </summary>
    /// <returns>The code and its arguments.</returns>
    public override string ToString() => Arguments.Count == 0 ? Code : $"{Code}({string.Join(", ", Arguments)})";
}
