using System.Text.Json;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using MediaFileClassifier.Infrastructure.Abstractions.Settings;
using Microsoft.Extensions.Logging;

namespace MediaFileClassifier.Infrastructure.Settings;

/// <summary>
/// Stores <see cref="AppSettings"/> in a JSON file. Writes go to a temporary file that then replaces
/// the old one, so a crash mid-write never leaves a half-written file.
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
            return JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings)
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
