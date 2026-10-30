using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Binds a property to a UI string from the resource files: <c>Text="{l:Loc MainWindow_EmptyTitle}"</c>
/// with <c>xmlns:l="using:MediaFileClassifier.Ui.Localization"</c>. The value follows culture and
/// pseudo-localisation switches.
/// </summary>
public sealed class LocExtension : MarkupExtension
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class.
    /// </summary>
    public LocExtension()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LocExtension"/> class.
    /// </summary>
    /// <param name="key">Resource key.</param>
    public LocExtension(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets or sets the resource key.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <inheritdoc />
    public override BindingBase ProvideValue(IServiceProvider serviceProvider)
        => Localizer.Current.Observe(Key).ToBinding();
}
