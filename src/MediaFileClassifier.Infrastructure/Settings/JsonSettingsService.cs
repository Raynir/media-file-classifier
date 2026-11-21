using System.Text.Json;
using System.Text.Json.Nodes;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> in a JSON file. Writes go to a temporary file that then replaces
/// the old one, so a crash mid-write never leaves a half-written file. Files of older schema versions
/// are migrated when loaded.
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string settingsFile;
    private readonly ILogger<JsonSettingsService> logger;
    private readonly Lock syncRoot = new();
    private AppSettings? current;

    public JsonSettingsService(IAppPaths appPaths, ILogger<JsonSettingsService> logger)
    {
        settingsFile = appPaths.SettingsFile;
        this.logger = logger;
    }

    /// <inheritdoc />
    public AppSettings Current
    {
        get
        {
            lock (syncRoot)
            {
                return current ??= Load();
            }
        }
    }

    /// <inheritdoc />
    public void Save(AppSettings settings)
    {
        lock (syncRoot)
        {
            Write(settings);
        }
    }

    /// <inheritdoc />
    public AppSettings Update(Func<AppSettings, AppSettings> change)
    {
        lock (syncRoot)
        {
            var settings = change(current ??= Load());
            Write(settings);
            return settings;
        }
    }

    /// <summary>
    /// Brings a settings file of an older schema version up to <see cref="AppSettings.CurrentSchemaVersion"/>.
    /// </summary>
    /// <param name="root">The file's root object; changed in place.</param>
    internal static void Migrate(JsonObject root)
    {
        // A malformed value is left for deserialization to reject, so Load treats the file as corrupt.
        var version = root["schemaVersion"] is JsonValue schemaVersion
            && schemaVersion.GetValueKind() == JsonValueKind.Number
            && schemaVersion.TryGetValue<int>(out var number)
                ? number
                : 1;
        if (version < 2)
        {
            // Version 1 had providers.openAiCompatibleEndpoint; version 2 keeps it with the other registered settings.
            if (root["providers"] is JsonObject providers
                && providers["openAiCompatibleEndpoint"] is JsonValue endpoint
                && endpoint.GetValueKind() == JsonValueKind.String
                && endpoint.GetValue<string>().Length > 0)
            {
                if (root["values"] is not JsonObject values)
                {
                    values = [];
                    root["values"] = values;
                }

                values[KnownSettings.OpenAiCompatibleEndpoint.Key] ??= endpoint.GetValue<string>();
            }

            root.Remove("providers");
        }

        MoveProcessingIntoValues(root);
    }

    // The typed "processing" section (before the processing settings were registered) moves into the registered values,
    // under their keys. It runs on every load, so no schema bump is needed; a value the setting rejects falls back to its default.
    private static void MoveProcessingIntoValues(JsonObject root)
    {
        if (!root.Remove("processing", out var node) || node is not JsonObject processing)
        {
            return;
        }

        if (root["values"] is not JsonObject values)
        {
            values = [];
            root["values"] = values;
        }

        foreach (var (property, key) in new (string, string)[]
        {
            ("ioConcurrency", "processing.ioConcurrency"),
            ("cpuConcurrency", "processing.cpuConcurrency"),
            ("aiConcurrency", "processing.aiConcurrency"),
            ("resumeOnOpen", "processing.resumeOnOpen"),
            ("confirmQuitWhileProcessing", "processing.confirmQuitWhileProcessing"),
        })
        {
            if (processing[property] is { } value && values[key] is null)
            {
                values[key] = value.DeepClone();
            }
        }
    }

    private void Write(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        var tempFile = settingsFile + ".tmp";
        using (var stream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings with { SchemaVersion = AppSettings.CurrentSchemaVersion }, SettingsJsonContext.Default.AppSettings);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(settingsFile))
        {
            File.Replace(tempFile, settingsFile, destinationBackupFileName: null);
        }
        else
        {
            File.Move(tempFile, settingsFile);
        }

        current = settings;
    }

    private AppSettings Load()
    {
        if (!File.Exists(settingsFile))
        {
            logger.LogInformation("No settings file at {SettingsFile}, using defaults", settingsFile);
            return new AppSettings();
        }

        try
        {
            using var stream = File.OpenRead(settingsFile);
            if (JsonNode.Parse(stream) is not JsonObject root)
            {
                throw new JsonException("The settings file doesn't contain a JSON object.");
            }

            Migrate(root);
            return root.Deserialize(SettingsJsonContext.Default.AppSettings)
                ?? throw new JsonException("The settings file contains null.");
        }
        catch (JsonException ex)
        {
            var corruptCopy = settingsFile + ".corrupt";
            File.Copy(settingsFile, corruptCopy, overwrite: true);
            logger.LogWarning(ex, "Settings file {SettingsFile} is corrupt, using defaults; kept a copy at {CorruptCopy}", settingsFile, corruptCopy);
            return new AppSettings();
        }
    }
}
