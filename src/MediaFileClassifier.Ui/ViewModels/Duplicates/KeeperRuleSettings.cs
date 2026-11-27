using System.Text.Json;
using MediaFileClassifier.Domain.Duplicates;
using MediaFileClassifier.Infrastructure.Abstractions.Libraries;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;

namespace MediaFileClassifier.Ui.ViewModels.Duplicates;

/// <summary>
/// Keeps the order of the keeper rules per library in <c>settings.json</c> (E12-S03-T03), under a key made of the
/// library's ID, so a library needs no schema change for it. A library without a saved order uses the default one.
/// </summary>
/// <param name="settings">The settings.</param>
/// <param name="session">Holds the open library.</param>
public sealed class KeeperRuleSettings(ISettingsService settings, ILibrarySession session)
{
    private const string KeyPrefix = "duplicates.keeperRules.";

    /// <summary>
    /// Gets the open library's rule order, complete as <see cref="KeeperRules.Normalize"/> makes it.
    /// </summary>
    /// <returns>The order; the default one without an open library, without a saved order, or when the saved value is unusable.</returns>
    public IReadOnlyList<KeeperRule> Load()
    {
        if (Key() is not { } key || !settings.Current.Values.TryGetValue(key, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return KeeperRules.Default;
        }

        List<KeeperRule> saved = [];
        foreach (var element in value.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && Enum.TryParse<KeeperRule>(element.GetString(), out var rule))
            {
                saved.Add(rule);
            }
        }

        return KeeperRules.Normalize(saved);
    }

    /// <summary>
    /// Saves the open library's rule order. The default order removes the entry.
    /// </summary>
    /// <param name="order">The order.</param>
    public void Save(IEnumerable<KeeperRule> order)
    {
        ArgumentNullException.ThrowIfNull(order);

        if (Key() is not { } key)
        {
            return;
        }

        var complete = KeeperRules.Normalize(order);
        if (complete.SequenceEqual(KeeperRules.Default))
        {
            settings.Update(current => current with { Values = current.Values.Without(key) });
            return;
        }

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(complete.Select(rule => rule.ToString())));
        var element = json.RootElement.Clone();
        settings.Update(current => current with { Values = current.Values.With(key, element) });
    }

    private string? Key() => session.Current is { } library
        ? string.Concat(KeyPrefix, library.Library.Id.ToString())
        : null;
}
