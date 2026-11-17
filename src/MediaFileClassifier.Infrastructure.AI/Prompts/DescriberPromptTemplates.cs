using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.AI;
using MediaFileClassifier.Infrastructure.AI.StructuredOutput;

namespace MediaFileClassifier.Infrastructure.AI.Prompts;

/// <summary>
/// One version of the vision-language model describer's prompt (E06-S04-T04), loaded from an embedded
/// <c>Prompts/&lt;version&gt;.json</c>. The version is stored with every analysis run, so a run described with another
/// version is outdated (D358). Changing a template's text means a new version: a test pins each template's
/// <see cref="ContentHash"/>.
/// </summary>
/// <param name="Version">The version, e.g. <c>describe-1</c>.</param>
/// <param name="System">The system message.</param>
/// <param name="User">The request, with <c>{language}</c> (the output language's English name, D035) and <c>{max_list}</c>.</param>
/// <param name="Context">Added after the request when there's context, with <c>{context}</c>.</param>
public sealed record DescriberPromptTemplate(string Version, string System, string User, string Context)
{
    /// <summary>
    /// Gets the SHA-256 of the template's text (system, request and context joined with new lines), in lower-case hex.
    /// It doesn't depend on the resource file's line endings.
    /// </summary>
    public string ContentHash =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', System, User, Context))));

    /// <summary>
    /// Makes the request text for one description.
    /// </summary>
    /// <param name="options">The request options.</param>
    /// <returns>The request.</returns>
    public string BuildRequest(ImageDescriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = User
            .Replace("{language}", GetLanguageName(options.Language), StringComparison.Ordinal)
            .Replace("{max_list}", ImageDescriptionContract.MaxListItems.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return options.Context is null ? request : $"{request} {Context.Replace("{context}", options.Context, StringComparison.Ordinal)}";
    }

    private static string GetLanguageName(string? code)
    {
        if (code is null)
        {
            return "English";
        }

        try
        {
            return CultureInfo.GetCultureInfo(code).EnglishName;
        }
        catch (CultureNotFoundException)
        {
            return code;
        }
    }
}

/// <summary>
/// The describer prompt templates shipped with the app (E06-S04-T04). A new version is a new embedded
/// <c>Prompts/&lt;version&gt;.json</c> file and an entry in <see cref="Versions"/>; the last entry is the default.
/// </summary>
public static class DescriberPromptTemplates
{
    /// <summary>
    /// The shipped versions, oldest first. The last one is <see cref="Latest"/>.
    /// </summary>
    /// <remarks>Part D of E06-S04 adds the vision-language model prompt from the describer spike (MFC-105) here.</remarks>
    public static readonly IReadOnlyList<string> Versions = ["describe-1"];

    private static readonly Lazy<IReadOnlyDictionary<string, DescriberPromptTemplate>> Templates = new(Load);

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true,
    };

    /// <summary>
    /// Gets the newest template, used when the profile asks for none or for one the app doesn't have.
    /// </summary>
    public static DescriberPromptTemplate Latest => Templates.Value[Versions[^1]];

    /// <summary>
    /// Gets every shipped template, oldest first.
    /// </summary>
    public static IEnumerable<DescriberPromptTemplate> All => Versions.Select(version => Templates.Value[version]);

    /// <summary>
    /// Picks the template for the version the analysis profile asks for.
    /// </summary>
    /// <param name="requested">The profile's prompt version, or <c>null</c> for the newest.</param>
    /// <returns>That template, or <see cref="Latest"/> when it's <c>null</c> or unknown.</returns>
    public static DescriberPromptTemplate Resolve(string? requested) =>
        requested is not null && Templates.Value.TryGetValue(requested, out var template) ? template : Latest;

    private static Dictionary<string, DescriberPromptTemplate> Load()
    {
        var result = new Dictionary<string, DescriberPromptTemplate>(StringComparer.Ordinal);
        foreach (var version in Versions)
        {
            var name = $"MediaFileClassifier.Infrastructure.AI.Prompts.{version}.json";
            using var stream = typeof(DescriberPromptTemplates).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The prompt template {name} isn't embedded.");
            var template = JsonSerializer.Deserialize<DescriberPromptTemplate>(stream, SerializerOptions)
                ?? throw new InvalidOperationException($"The prompt template {name} is empty.");
            if (!string.Equals(template.Version, version, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The prompt template {name} says it's version {template.Version}.");
            }

            result.Add(version, template);
        }

        return result;
    }
}
