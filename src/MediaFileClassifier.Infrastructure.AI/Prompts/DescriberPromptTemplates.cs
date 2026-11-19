using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// <param name="User">
/// The request, with <c>{language}</c> (the output language's English name, D035), <c>{max_list}</c>, <c>{max_tags}</c>,
/// and optionally <c>{opening}</c> (<paramref name="Image"/> or <paramref name="Clip"/>) and <c>{context}</c> (where
/// <paramref name="Context"/> goes; without it the context goes at the end).
/// </param>
/// <param name="Context">The context line, with <c>{context}</c>; left out when there's no context.</param>
/// <param name="Schema">The output schema version the template asks for (<see cref="ImageDescriptionContract"/>).</param>
/// <param name="Image">The opening request for one picture, or <c>null</c> when the request has no <c>{opening}</c>.</param>
/// <param name="Clip">
/// The opening request for several frames of one clip, with <c>{count}</c>, or <c>null</c>: the default clip sentence
/// then goes before the context.
/// </param>
public sealed partial record DescriberPromptTemplate(
    string Version,
    string System,
    string User,
    string Context,
    int Schema = 1,
    string? Image = null,
    string? Clip = null)
{
    /// <summary>
    /// The clip sentence for templates without <see cref="Clip"/>, with <c>{count}</c>.
    /// </summary>
    public const string DefaultClip = "The {count} pictures are frames of one video or animation, in time order. Describe the whole clip, not each frame.";

    /// <summary>
    /// Gets the SHA-256 of the template's text (system, request and context joined with new lines, then the opening
    /// and clip requests and the schema version when the template has them), in lower-case hex. It doesn't depend on
    /// the resource file's line endings.
    /// </summary>
    public string ContentHash
    {
        get
        {
            var parts = new List<string> { System, User, Context };
            if (Image is not null)
            {
                parts.Add(Image);
            }

            if (Clip is not null)
            {
                parts.Add(Clip);
            }

            if (Schema != 1)
            {
                parts.Add($"schema {Schema.ToString(CultureInfo.InvariantCulture)}");
            }

            var text = string.Join('\n', parts).ReplaceLineEndings("\n");
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }

    /// <summary>
    /// Makes the request text for one description.
    /// </summary>
    /// <param name="options">The request options.</param>
    /// <returns>The request.</returns>
    public string BuildRequest(ImageDescriptionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var count = options.FrameCount.ToString(CultureInfo.InvariantCulture);
        var isClip = options.FrameCount > 1;
        var context = options.Context;
        if (isClip && Clip is null)
        {
            var clip = DefaultClip.Replace("{count}", count, StringComparison.Ordinal);
            context = context is null ? clip : $"{clip} {context}";
        }

        var contextLine = context is null ? string.Empty : Context.Replace("{context}", context, StringComparison.Ordinal);
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["language"] = GetLanguageName(options.Language),
            ["max_list"] = ImageDescriptionContract.MaxListItems.ToString(CultureInfo.InvariantCulture),
            ["max_tags"] = ImageDescriptionContract.MaxTagItems.ToString(CultureInfo.InvariantCulture),
            ["opening"] = (isClip ? Clip?.Replace("{count}", count, StringComparison.Ordinal) : null) ?? Image ?? string.Empty,
            ["context"] = contextLine,
        };

        // One pass, so a placeholder inside the caller's context isn't filled in.
        var request = Placeholder().Replace(User, match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : match.Value);
        return context is null || User.Contains("{context}", StringComparison.Ordinal) ? request : $"{request} {contextLine}";
    }

    [GeneratedRegex(@"\{([a-z_]+)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

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
    /// <remarks>describe-2 is prompt C of the describer spike (MFC-105), with Russian title and tag forms (schema 2).</remarks>
    public static readonly IReadOnlyList<string> Versions = ["describe-1", "describe-2"];

    // PublicationOnly: a broken template throws on every use rather than once, then cached for the process.
    private static readonly Lazy<IReadOnlyDictionary<string, DescriberPromptTemplate>> Templates = new(Load, LazyThreadSafetyMode.PublicationOnly);

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
