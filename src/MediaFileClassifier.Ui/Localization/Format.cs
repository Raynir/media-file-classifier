using System.Globalization;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Formats numbers, dates, durations and file sizes for display, using <see cref="CultureInfo.CurrentCulture"/>
/// unless a culture is passed (E13-S08-T02). Views use <see cref="FormatConverters"/>.
/// </summary>
public static class Format
{
    private const double Kilobyte = 1024;

    private static readonly string[] SizeUnitKeys =
    [
        "FileSize_Kilobytes",
        "FileSize_Megabytes",
        "FileSize_Gigabytes",
        "FileSize_Terabytes",
    ];

    /// <summary>
    /// Formats a whole number with group separators, e.g. <c>12,345</c> or <c>12 345</c>.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted number.</returns>
    public static string Number(long value, CultureInfo? culture = null)
        => value.ToString("N0", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a number with a fixed number of decimals, e.g. <c>3.50</c> or <c>3,50</c>.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <param name="decimals">Number of decimals.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted number.</returns>
    public static string Number(double value, int decimals, CultureInfo? culture = null)
        => value.ToString("N" + decimals.ToString(CultureInfo.InvariantCulture), culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats the date part in the culture's short date pattern, e.g. <c>3/4/2026</c> or <c>04.03.2026</c>.
    /// </summary>
    /// <param name="value">Date and time. Shown in local time.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted date.</returns>
    public static string Date(DateTimeOffset value, CultureInfo? culture = null)
        => value.ToLocalTime().ToString("d", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a date and time in the culture's short date and short time patterns, e.g. <c>3/4/2026 2:05 PM</c>.
    /// </summary>
    /// <param name="value">Date and time. Shown in local time.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted date and time.</returns>
    public static string DateTime(DateTimeOffset value, CultureInfo? culture = null)
        => value.ToLocalTime().ToString("g", culture ?? CultureInfo.CurrentCulture);

    /// <summary>
    /// Formats a media duration as <c>m:ss</c>, or <c>h:mm:ss</c> from one hour.
    /// </summary>
    /// <param name="value">The duration. Negative values are shown as zero.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted duration.</returns>
    public static string Duration(TimeSpan value, CultureInfo? culture = null)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var pattern = value.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss";
        return value.ToString(pattern, culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a moment in a video as <c>mm:ss</c>, or <c>h:mm:ss</c> from one hour, e.g. <c>02:37</c>: the form to
    /// type or paste into a player's "go to time" box.
    /// </summary>
    /// <param name="value">The time. Negative values are shown as zero.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted time.</returns>
    public static string Timestamp(TimeSpan value, CultureInfo? culture = null)
    {
        if (value < TimeSpan.Zero)
        {
            value = TimeSpan.Zero;
        }

        var pattern = value.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss";
        return value.ToString(pattern, culture ?? CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// Formats a file size in binary units, as Windows Explorer does: <c>512 bytes</c>, <c>1.5 KB</c>,
    /// <c>15.2 MB</c>, <c>152 MB</c>. One decimal is shown below 100 units.
    /// </summary>
    /// <param name="bytes">Size in bytes.</param>
    /// <param name="culture">Culture, or <c>null</c> for the current culture.</param>
    /// <returns>The formatted size.</returns>
    public static string FileSize(long bytes, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var localizer = Localizer.Current;
        if (Math.Abs(bytes) < Kilobyte)
        {
            return string.Format(culture, localizer.Get("FileSize_Bytes"), Number(bytes, culture));
        }

        double value = bytes;
        var unit = -1;
        while (Math.Abs(value) >= Kilobyte && unit < SizeUnitKeys.Length - 1)
        {
            value /= Kilobyte;
            unit++;
        }

        var number = Math.Abs(value) < 100 ? Number(value, 1, culture) : Number(value, 0, culture);
        return string.Format(culture, localizer.Get(SizeUnitKeys[unit]), number);
    }
}
