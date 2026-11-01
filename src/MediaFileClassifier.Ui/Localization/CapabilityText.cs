using MediaFileClassifier.Infrastructure.Abstractions.Capabilities;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Turns capability names and reason codes into UI text (E13-S08). The layers below the UI report codes only;
/// the strings live in <c>Resources/Strings.resx</c>.
/// </summary>
public static class CapabilityText
{
    /// <summary>
    /// Resource key of the message for an unavailable capability, with the name and the reason as placeholders.
    /// </summary>
    public const string UnavailableKey = "Capability_Unavailable";

    /// <summary>
    /// Gets the resource key of a capability's name.
    /// </summary>
    /// <param name="capability">The capability.</param>
    /// <returns>The key, <c>Capability_&lt;Name&gt;</c>.</returns>
    public static string NameKey(CapabilityDefinition capability) => "Capability_" + capability.Name;

    /// <summary>
    /// Gets the resource key of a reason code.
    /// </summary>
    /// <param name="code">A <see cref="CapabilityReasons"/> code.</param>
    /// <returns>The key, <c>CapabilityReason_&lt;Code&gt;</c>.</returns>
    public static string ReasonKey(string code) => "CapabilityReason_" + code;

    /// <summary>
    /// Describes why a capability is unavailable, e.g. "Video analysis unavailable: FFmpeg not found. …".
    /// </summary>
    /// <param name="localizer">The localizer.</param>
    /// <param name="capability">The capability.</param>
    /// <param name="reason">Why it's unavailable.</param>
    /// <returns>The message.</returns>
    public static string Describe(Localizer localizer, CapabilityDefinition capability, CapabilityReason reason)
        => localizer.Format(
            UnavailableKey,
            localizer.Get(NameKey(capability)),
            localizer.Format(ReasonKey(reason.Code), [.. reason.Arguments]));
}
