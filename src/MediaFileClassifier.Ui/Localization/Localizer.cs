using System.Globalization;
using System.Resources;

namespace MediaFileClassifier.Ui.Localization;

/// <summary>
/// Looks up UI strings in the resource files (E13-S08). Views bind strings with
/// <see cref="LocExtension"/>, and code calls <see cref="Get"/> or <see cref="Format"/>.
/// Bound strings update when <see cref="Culture"/> changes.
/// </summary>
public sealed class Localizer
{
    private readonly ResourceManager resources;
    private CultureInfo? culture;

    /// <summary>
    /// Initializes a new instance of the <see cref="Localizer"/> class.
    /// </summary>
    /// <param name="resources">The resource files to read.</param>
    public Localizer(ResourceManager resources)
    {
        this.resources = resources;
    }

    /// <summary>
    /// Raised when the strings change, after a culture switch.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Gets the app's localizer, which reads <c>Resources/Strings.resx</c>. XAML markup extensions
    /// can't use dependency injection, so the app uses this one instance everywhere.
    /// </summary>
    public static Localizer Current { get; } = new(
        new ResourceManager("MediaFileClassifier.Ui.Resources.Strings", typeof(Localizer).Assembly));

    /// <summary>
    /// Gets or sets the UI culture, or <c>null</c> to follow <see cref="CultureInfo.CurrentUICulture"/>.
    /// </summary>
    public CultureInfo? Culture
    {
        get => culture;
        set
        {
            culture = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Gets the string with the given key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The string, or the key in <c>!!</c> markers when it's missing, so the gap is visible.</returns>
    public string this[string key] => Get(key);

    /// <summary>
    /// Gets the string with the given key.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>The string, or the key in <c>!!</c> markers when it's missing, so the gap is visible.</returns>
    public string Get(string key)
    {
        var value = resources.GetString(key, culture ?? CultureInfo.CurrentUICulture);
        return value ?? $"!{key}!";
    }

    /// <summary>
    /// Gets a composite format string and fills in its placeholders, formatting the arguments
    /// for <see cref="CultureInfo.CurrentCulture"/>.
    /// </summary>
    /// <param name="key">Resource key of a composite format string, such as <c>"{0} KB"</c>.</param>
    /// <param name="args">Values for the placeholders.</param>
    /// <returns>The formatted string.</returns>
    public string Format(string key, params object?[] args)
        => string.Format(CultureInfo.CurrentCulture, Get(key), args);

    /// <summary>
    /// Gets the string with the given key now and again after every <see cref="Changed"/>.
    /// </summary>
    /// <param name="key">Resource key.</param>
    /// <returns>An observable of the string.</returns>
    public IObservable<string> Observe(string key) => new KeyObservable(this, key);

    private sealed class KeyObservable(Localizer localizer, string key) : IObservable<string>
    {
        public IDisposable Subscribe(IObserver<string> observer)
        {
            void OnChanged(object? sender, EventArgs e) => observer.OnNext(localizer.Get(key));

            observer.OnNext(localizer.Get(key));
            localizer.Changed += OnChanged;
            return new Subscription(() => localizer.Changed -= OnChanged);
        }
    }

    private sealed class Subscription(Action unsubscribe) : IDisposable
    {
        private Action? unsubscribe = unsubscribe;

        public void Dispose()
        {
            unsubscribe?.Invoke();
            unsubscribe = null;
        }
    }
}
