using Avalonia.Data.Converters;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Value converters that apply <see cref="Format"/> in bindings, e.g.
/// <c>Text="{Binding Size, Converter={x:Static l:FormatConverters.FileSize}}"</c>.
/// </summary>
public static class FormatConverters
{
    /// <summary>
    /// Gets a converter for a whole number, see <see cref="Format.Number(long, System.Globalization.CultureInfo?)"/>.
    /// </summary>
    public static IValueConverter Number { get; } =
        new FuncValueConverter<long, string>(value => Format.Number(value));

    /// <summary>
    /// Gets a converter for a date, see <see cref="Format.Date"/>.
    /// </summary>
    public static IValueConverter Date { get; } =
        new FuncValueConverter<DateTimeOffset, string>(value => Format.Date(value));

    /// <summary>
    /// Gets a converter for a date and time, see <see cref="Format.DateTime"/>.
    /// </summary>
    public static IValueConverter DateTime { get; } =
        new FuncValueConverter<DateTimeOffset, string>(value => Format.DateTime(value));

    /// <summary>
    /// Gets a converter for a media duration, see <see cref="Format.Duration"/>.
    /// </summary>
    public static IValueConverter Duration { get; } =
        new FuncValueConverter<TimeSpan, string>(value => Format.Duration(value));

    /// <summary>
    /// Gets a converter for a file size in bytes, see <see cref="Format.FileSize"/>.
    /// </summary>
    public static IValueConverter FileSize { get; } =
        new FuncValueConverter<long, string>(value => Format.FileSize(value));
}
