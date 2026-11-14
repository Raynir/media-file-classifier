using MediaFileClassifier.Infrastructure.Abstractions.Search.Filters;

namespace MediaFileClassifier.Ui.Search.Filters;

/// <summary>
/// Finds the presets a filter offers (E10-S02-T01): from <see cref="IFilterPresets"/>, else from its
/// <see cref="IFilterOptions"/> when those answer without waiting (the presets are a fixed list).
/// </summary>
internal static class FilterPresetList
{
    public static IReadOnlyList<FilterPreset> Of(IFilterDefinition definition)
    {
        if (definition is IFilterPresets presets)
        {
            return presets.Presets;
        }

        if (definition is IFilterOptions options)
        {
            var task = options.GetOptionsAsync(null, CancellationToken.None);
            if (task.IsCompletedSuccessfully)
            {
                return [.. task.Result.Select(option => new FilterPreset(option.Value, option.DisplayNameKey ?? option.Value))];
            }
        }

        return [];
    }
}
