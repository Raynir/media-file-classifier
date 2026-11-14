using CommunityToolkit.Mvvm.ComponentModel;
using MediaFileClassifier.Domain.Analysis;
using MediaFileClassifier.Ui.Localization;
using MediaFileClassifier.UseCases.Analysis.GetAnalysisProfile;

namespace MediaFileClassifier.Ui.ViewModels.Analysis;

/// <summary>
/// An option of a drop-down of the analysis profile editor.
/// </summary>
/// <param name="Value">The provider ID, model name or language code; <see langword="null"/> for "automatic" or "any".</param>
/// <param name="Label">The text shown.</param>
public sealed record ProfileOption(string? Value, string Label);

/// <summary>
/// How serious the note under an analyzer is.
/// </summary>
public enum AnalyzerNoteKind
{
    /// <summary>No note.</summary>
    None,

    /// <summary>Information, such as "turned off".</summary>
    Info,

    /// <summary>The analyzer can't run until the user acts.</summary>
    Warning,
}

/// <summary>
/// One analyzer in the analysis profile editor (E17-S02-T01): a switch, the provider and model, and the options the
/// analyzer has. Changing anything asks the editor to save the profile.
/// </summary>
public sealed partial class AnalyzerCardViewModel : ViewModelBase
{
    private readonly Localizer localizer;
    private readonly Action<AnalyzerCardViewModel> changed;
    private AnalyzerChoices choices = new(string.Empty, []);
    private bool updating;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnalyzerCardViewModel"/> class.
    /// </summary>
    /// <param name="analyzer">Analyzer key, see <see cref="AnalyzerKeys"/>.</param>
    /// <param name="localizer">UI strings.</param>
    /// <param name="changed">Called when the user changed something.</param>
    public AnalyzerCardViewModel(string analyzer, Localizer localizer, Action<AnalyzerCardViewModel> changed)
    {
        Analyzer = analyzer;
        this.localizer = localizer;
        this.changed = changed;
        Languages =
        [
            new ProfileOption(null, localizer.Get("Profile_LanguageApp")),
            new ProfileOption("en", localizer.Get("Profile_LanguageEnglish")),
            new ProfileOption("ru", localizer.Get("Profile_LanguageRussian")),
        ];
        SelectedLanguage = Languages[0];
    }

    /// <summary>
    /// Gets the analyzer key.
    /// </summary>
    public string Analyzer { get; }

    /// <summary>
    /// Gets the analyzer's name.
    /// </summary>
    public string Title => localizer.Get("Profile_Analyzer_" + Analyzer);

    /// <summary>
    /// Gets what the analyzer does.
    /// </summary>
    public string Description => localizer.Get("Profile_AnalyzerHelp_" + Analyzer);

    /// <summary>
    /// Gets a value indicating whether the analyzer has the describer's options (tag limit and language).
    /// </summary>
    public bool HasDescriberOptions => Analyzer == AnalyzerKeys.Describer;

    /// <summary>
    /// Gets the languages descriptions can be written in.
    /// </summary>
    public IReadOnlyList<ProfileOption> Languages { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the analyzer runs.
    /// </summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets the provider choices.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProfileOption> ProviderOptions { get; private set; } = [];

    /// <summary>
    /// Gets or sets the chosen provider.
    /// </summary>
    [ObservableProperty]
    public partial ProfileOption? SelectedProvider { get; set; }

    /// <summary>
    /// Gets the model choices of the chosen provider, or of every provider on Automatic.
    /// </summary>
    [ObservableProperty]
    public partial IReadOnlyList<ProfileOption> ModelOptions { get; private set; } = [];

    /// <summary>
    /// Gets or sets the chosen model.
    /// </summary>
    [ObservableProperty]
    public partial ProfileOption? SelectedModel { get; set; }

    /// <summary>
    /// Gets or sets the most AI tags to keep, or <see langword="null"/> for no limit.
    /// </summary>
    [ObservableProperty]
    public partial decimal? MaxTags { get; set; }

    /// <summary>
    /// Gets or sets the chosen language.
    /// </summary>
    [ObservableProperty]
    public partial ProfileOption? SelectedLanguage { get; set; }

    /// <summary>
    /// Gets the note under the analyzer, such as why it can't run.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(IsWarning), nameof(IsInfo))]
    public partial string Note { get; private set; } = string.Empty;

    /// <summary>
    /// Gets how serious <see cref="Note"/> is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNote), nameof(IsWarning), nameof(IsInfo))]
    public partial AnalyzerNoteKind NoteKind { get; private set; }

    /// <summary>
    /// Gets a value indicating whether there is a note.
    /// </summary>
    public bool HasNote => NoteKind != AnalyzerNoteKind.None && Note.Length > 0;

    /// <summary>
    /// Gets a value indicating whether the note is a warning.
    /// </summary>
    public bool IsWarning => HasNote && NoteKind == AnalyzerNoteKind.Warning;

    /// <summary>
    /// Gets a value indicating whether the note is information.
    /// </summary>
    public bool IsInfo => HasNote && NoteKind == AnalyzerNoteKind.Info;

    /// <summary>
    /// Builds the analyzer's settings from what the card shows, keeping what the editor doesn't edit.
    /// </summary>
    /// <param name="existing">The analyzer's current settings, or <see langword="null"/>.</param>
    /// <returns>The settings.</returns>
    public AnalyzerSettings ToSettings(AnalyzerSettings? existing)
    {
        var settings = (existing ?? new AnalyzerSettings(Analyzer)) with
        {
            Enabled = Enabled,
            ProviderId = SelectedProvider?.Value,
            ModelId = SelectedModel?.Value,
        };
        return HasDescriberOptions
            ? settings with { MaxTags = MaxTags is { } count ? (int)count : null, Language = SelectedLanguage?.Value }
            : settings;
    }

    /// <summary>
    /// Shows the analyzer's stored settings and the providers that can run it, without raising a change.
    /// </summary>
    /// <param name="settings">The settings in the profile, or <see langword="null"/> when the profile lacks the analyzer.</param>
    /// <param name="providers">The providers for the analyzer.</param>
    public void Show(AnalyzerSettings? settings, AnalyzerChoices providers)
    {
        choices = providers;
        updating = true;
        try
        {
            Enabled = settings?.Enabled ?? true;
            var providerId = settings?.ProviderId;
            var options = new List<ProfileOption> { new(null, localizer.Get("Profile_Automatic")) };
            options.AddRange(providers.Providers.Select(provider => new ProfileOption(provider.Id, provider.DisplayName)));
            if (providerId is not null && options.All(option => option.Value != providerId))
            {
                options.Add(new ProfileOption(providerId, localizer.Format("Profile_MissingProvider", providerId)));
            }

            ProviderOptions = options;
            SelectedProvider = options.First(option => option.Value == providerId);

            var modelId = settings?.ModelId;
            var candidates = providers.Providers.Where(provider => providerId is null || provider.Id == providerId).ToList();
            var models = new List<ProfileOption> { new(null, localizer.Get("Profile_AnyModel")) };
            models.AddRange(candidates.Select(provider => provider.ModelName).Distinct(StringComparer.Ordinal).Select(name => new ProfileOption(name, name)));
            if (modelId is not null && models.All(option => option.Value != modelId))
            {
                models.Add(new ProfileOption(modelId, localizer.Format("Profile_MissingModel", modelId)));
            }

            ModelOptions = models;
            SelectedModel = models.First(option => option.Value == modelId);

            MaxTags = settings?.MaxTags;
            SelectedLanguage = Languages.FirstOrDefault(option => option.Value == settings?.Language) ?? Languages[0];
        }
        finally
        {
            updating = false;
        }

        ShowNote(providerId: settings?.ProviderId, modelId: settings?.ModelId);
    }

    /// <summary>
    /// Raises the localized texts again after the language changed.
    /// </summary>
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }

    partial void OnEnabledChanged(bool value) => Raise();

    partial void OnSelectedProviderChanged(ProfileOption? value)
    {
        if (updating)
        {
            return;
        }

        // The models on offer follow the provider; the old model may not exist there.
        var settings = ToSettings(null);
        Show(settings with { ModelId = null }, choices);
        changed(this);
    }

    partial void OnSelectedModelChanged(ProfileOption? value) => Raise();

    partial void OnMaxTagsChanged(decimal? value) => Raise();

    partial void OnSelectedLanguageChanged(ProfileOption? value) => Raise();

    private void Raise()
    {
        if (!updating)
        {
            ShowNote(SelectedProvider?.Value, SelectedModel?.Value);
            changed(this);
        }
    }

    private void ShowNote(string? providerId, string? modelId)
    {
        var candidates = choices.Providers
            .Where(provider => providerId is null || provider.Id == providerId)
            .Where(provider => modelId is null || provider.ModelName == modelId)
            .ToList();
        var usable = candidates.Where(provider => provider.SupportedHere).ToList();

        if (!Enabled)
        {
            (Note, NoteKind) = (localizer.Get("Profile_NoteDisabled"), AnalyzerNoteKind.Info);
        }
        else if (providerId is not null && choices.Providers.All(provider => provider.Id != providerId))
        {
            (Note, NoteKind) = (localizer.Get("Profile_NoteProviderMissing"), AnalyzerNoteKind.Warning);
        }
        else if (usable.Count == 0)
        {
            (Note, NoteKind) = (localizer.Get("Profile_NoteNoProvider"), AnalyzerNoteKind.Warning);
        }
        else if (usable.All(provider => provider.Health is { IsHealthy: false }))
        {
            var health = usable[0].Health!;
            (Note, NoteKind) = (health.Message ?? localizer.Get("Profile_NoteUnhealthy"), AnalyzerNoteKind.Warning);
        }
        else
        {
            (Note, NoteKind) = (string.Empty, AnalyzerNoteKind.None);
        }
    }
}
