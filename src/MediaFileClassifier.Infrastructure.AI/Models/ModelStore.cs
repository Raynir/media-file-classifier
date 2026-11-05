using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaFileClassifier.Infrastructure.Abstractions.AI.Models;
using MediaFileClassifier.Infrastructure.Abstractions.Paths;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MediaFileClassifier.Infrastructure.AI.Models;

/// <summary>
/// The per-user model store on disk (D032, E01-S05-T06).
/// </summary>
/// <remarks>
/// <para>
/// Layout: <c>models\&lt;id&gt;\&lt;version&gt;\</c> holds the files and <c>manifest.json</c>; <c>models\usage.json</c>
/// maps each model to the libraries that use it; <c>models\.staging</c> and <c>models\.trash</c> hold imports in
/// progress and deletions in progress.
/// </para>
/// <para>
/// Safety: IDs, versions and file names are restricted to plain names, so no path can leave the store. An import is
/// assembled in <c>.staging</c> and moved into place only after every hash checks out, so a half-imported model is
/// never visible. A deletion first moves the folder to <c>.trash</c> in one step, so a model is either complete or
/// gone. Operations that change the store run one at a time.
/// </para>
/// </remarks>
public sealed partial class ModelStore : IModelStore, IDisposable
{
    private const string ManifestFileName = "manifest.json";
    private const string UsageFileName = "usage.json";
    private const string StagingFolderName = ".staging";
    private const string TrashFolderName = ".trash";
    private const int MaxNameLength = 100;
    private const int BufferSize = 1 << 20;

    private static readonly TimeSpan AbandonedStagingAge = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly TimeProvider timeProvider;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelStore"/> class in the app's data folder.
    /// </summary>
    /// <param name="paths">App folders.</param>
    /// <param name="timeProvider">Clock for installation times.</param>
    /// <param name="logger">Logger.</param>
    public ModelStore(IAppPaths paths, TimeProvider timeProvider, ILogger<ModelStore>? logger = null)
        : this(Path.Combine(paths?.DataDirectory ?? throw new ArgumentNullException(nameof(paths)), "models"), timeProvider, logger)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelStore"/> class in a given folder.
    /// </summary>
    /// <param name="rootDirectory">The store's root folder.</param>
    /// <param name="timeProvider">Clock for installation times.</param>
    /// <param name="logger">Logger.</param>
    internal ModelStore(string rootDirectory, TimeProvider timeProvider, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        this.timeProvider = timeProvider;
        this.logger = logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public string RootDirectory { get; }

    private string UsageFile => Path.Combine(RootDirectory, UsageFileName);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledModel>> GetInstalledAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(RootDirectory))
        {
            return [];
        }

        var models = new List<InstalledModel>();
        foreach (var modelDirectory in Directory.EnumerateDirectories(RootDirectory).Where(IsModelName).Order(StringComparer.Ordinal))
        {
            foreach (var versionDirectory in Directory.EnumerateDirectories(modelDirectory).Where(IsModelName).Order(StringComparer.Ordinal))
            {
                if (await TryReadManifestAsync(versionDirectory, cancellationToken).ConfigureAwait(false) is { } model)
                {
                    models.Add(model);
                }
            }
        }

        return models;
    }

    /// <inheritdoc />
    public Task<InstalledModel?> FindAsync(string modelId, string version, CancellationToken cancellationToken)
    {
        var directory = GetModelDirectory(modelId, version);
        return Directory.Exists(directory) ? TryReadManifestAsync(directory, cancellationToken) : Task.FromResult<InstalledModel?>(null);
    }

    /// <inheritdoc />
    public async Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = GetModelDirectory(request.ModelId, request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        var fileNames = ValidateFiles(request.Files);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Directory.Exists(target))
            {
                throw new ModelStoreException($"{request.DisplayName} {request.Version} is already installed. Delete it first to replace it.");
            }

            CleanUpLeftovers();
            var staging = Path.Combine(RootDirectory, StagingFolderName, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var files = new List<InstalledModelFile>();
                long processed = 0;
                for (var index = 0; index < request.Files.Count; index++)
                {
                    var source = request.Files[index];
                    var destination = Path.Combine(staging, fileNames[index]);
                    if (!File.Exists(source.SourcePath))
                    {
                        throw new ModelStoreException($"The file {source.SourcePath} doesn't exist.");
                    }

                    var offset = processed;
                    var onProgress = progress is null ? null : new Action<long>(bytes => progress.Report(offset + bytes));
                    string sha256;
                    long size;
                    if (request.MoveSourceFiles)
                    {
                        // Check the hash before the move, so a wrong file is never taken away from where the user keeps it.
                        (sha256, size) = await HashFileAsync(source.SourcePath, null, onProgress, cancellationToken).ConfigureAwait(false);
                        EnsureExpectedHash(fileNames[index], sha256, source.ExpectedSha256);
                        File.Move(source.SourcePath, destination);
                    }
                    else
                    {
                        (sha256, size) = await HashFileAsync(source.SourcePath, destination, onProgress, cancellationToken).ConfigureAwait(false);
                        EnsureExpectedHash(fileNames[index], sha256, source.ExpectedSha256);
                    }

                    processed += size;
                    files.Add(new InstalledModelFile(source.Role, fileNames[index], size, sha256));
                }

                var model = new InstalledModel(
                    request.ModelId,
                    request.Version,
                    request.DisplayName,
                    request.License,
                    request.Source,
                    target,
                    files,
                    ComputeContentHash(files),
                    timeProvider.GetUtcNow());
                await WriteJsonAtomicallyAsync(Path.Combine(staging, ManifestFileName), Manifest.From(model), cancellationToken).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                Directory.Move(staging, target);
                logger.LogInformation("Installed model {ModelId} {Version} ({FileCount} files)", model.ModelId, model.Version, files.Count);
                return model;
            }
            catch
            {
                TryDeleteDirectory(staging);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, CancellationToken cancellationToken)
    {
        var model = await FindAsync(modelId, version, cancellationToken).ConfigureAwait(false)
            ?? throw new ModelStoreException($"The model {modelId} {version} isn't installed.");

        var problems = new List<string>();
        foreach (var file in model.Files)
        {
            var path = Path.Combine(model.Directory, file.FileName);
            if (!File.Exists(path))
            {
                problems.Add($"{file.FileName} is missing.");
                continue;
            }

            var (sha256, size) = await HashFileAsync(path, null, null, cancellationToken).ConfigureAwait(false);
            if (size != file.Size || !string.Equals(sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{file.FileName} is damaged: its contents changed since it was installed.");
            }
        }

        return problems;
    }

    /// <inheritdoc />
    public async Task RecordUsageAsync(string modelId, string version, string libraryPath, CancellationToken cancellationToken)
    {
        var key = GetUsageKey(modelId, version);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        var library = Path.TrimEndingDirectorySeparator(Path.GetFullPath(libraryPath));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var usage = await ReadUsageAsync(cancellationToken).ConfigureAwait(false);
            var libraries = usage.TryGetValue(key, out var existing) ? existing : usage[key] = [];
            if (!libraries.Contains(library, PathComparer))
            {
                libraries.Add(library);
                await WriteJsonAtomicallyAsync(UsageFile, usage, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, CancellationToken cancellationToken)
    {
        var key = GetUsageKey(modelId, version);
        var usage = await ReadUsageAsync(cancellationToken).ConfigureAwait(false);
        return usage.TryGetValue(key, out var libraries) ? libraries : [];
    }

    /// <inheritdoc />
    public async Task<ModelDeletionResult> DeleteAsync(string modelId, string version, bool confirmed, CancellationToken cancellationToken)
    {
        var target = GetModelDirectory(modelId, version);
        var key = GetUsageKey(modelId, version);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!Directory.Exists(target))
            {
                throw new ModelStoreException($"The model {modelId} {version} isn't installed.");
            }

            var usage = await ReadUsageAsync(cancellationToken).ConfigureAwait(false);
            var libraries = usage.TryGetValue(key, out var existing) ? existing : [];
            if (libraries.Count > 0 && !confirmed)
            {
                return new ModelDeletionResult(false, libraries);
            }

            CleanUpLeftovers();
            var trash = Path.Combine(RootDirectory, TrashFolderName, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.GetDirectoryName(trash)!);
            try
            {
                Directory.Move(target, trash);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new ModelStoreException($"The model {modelId} {version} is in use. Stop the analysis that uses it, then try again.", ex);
            }

            if (usage.Remove(key))
            {
                await WriteJsonAtomicallyAsync(UsageFile, usage, cancellationToken).ConfigureAwait(false);
            }

            var modelDirectory = Path.GetDirectoryName(target)!;
            if (!Directory.EnumerateFileSystemEntries(modelDirectory).Any())
            {
                Directory.Delete(modelDirectory);
            }

            TryDeleteDirectory(trash);
            logger.LogInformation("Deleted model {ModelId} {Version}", modelId, version);
            return new ModelDeletionResult(true, libraries);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => gate.Dispose();

    /// <summary>
    /// Computes the hash that identifies a model's set of files.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <returns>SHA-256 over <c>role:sha256</c> lines in role order, in lower-case hex.</returns>
    internal static string ComputeContentHash(IEnumerable<InstalledModelFile> files)
    {
        var lines = string.Concat(files.OrderBy(file => file.Role, StringComparer.Ordinal).Select(file => $"{file.Role}:{file.Sha256.ToLowerInvariant()}\n"));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(lines)));
    }

    private static void EnsureExpectedHash(string fileName, string sha256, string? expected)
    {
        if (expected is not null && !string.Equals(sha256, expected.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ModelStoreException(
                $"{fileName} isn't the expected file, or it's damaged: its SHA-256 is {sha256}, but {expected.Trim().ToLowerInvariant()} was expected.");
        }
    }

    private static bool IsModelName(string path) => NamePattern().IsMatch(Path.GetFileName(path));

    private static void ValidateName(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > MaxNameLength || !NamePattern().IsMatch(value))
        {
            throw new ArgumentException(
                $"'{value}' isn't a valid name: use lower-case letters, digits, dots, dashes and underscores, starting with a letter or digit.",
                parameterName);
        }
    }

    private static List<string> ValidateFiles(IReadOnlyList<ModelFileSource>? files)
    {
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("A model needs at least one file.", nameof(files));
        }

        var roles = new HashSet<string>(StringComparer.Ordinal);
        var names = new List<string>();
        foreach (var file in files)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(file.Role, nameof(files));
            ArgumentException.ThrowIfNullOrWhiteSpace(file.SourcePath, nameof(files));
            if (!roles.Add(file.Role))
            {
                throw new ArgumentException($"Two files have the role '{file.Role}'.", nameof(files));
            }

            var name = file.FileName ?? Path.GetFileName(file.SourcePath);
            if (string.IsNullOrWhiteSpace(name)
                || name.Length > 200
                || name != Path.GetFileName(name)
                || name.StartsWith('.')
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.IndexOfAny(['/', '\\', ':']) >= 0
                || string.Equals(name, ManifestFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{name}' isn't a valid model file name.", nameof(files));
            }

            if (names.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Two files are named '{name}'.", nameof(files));
            }

            names.Add(name);
        }

        return names;
    }

    private static async Task<(string Sha256, long Size)> HashFileAsync(
        string source,
        string? copyTo,
        Action<long>? onProgress,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long size = 0;
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = copyTo is null
            ? null
            : new FileStream(copyTo, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            if (output is not null)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            size += read;
            onProgress?.Invoke(size);
        }

        if (output is not null)
        {
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return (Convert.ToHexStringLower(hash.GetHashAndReset()), size);
    }

    private static async Task WriteJsonAtomicallyAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporary, path, overwrite: true);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]*$")]
    private static partial Regex NamePattern();

    private string GetModelDirectory(string modelId, string version)
    {
        ValidateName(modelId, nameof(modelId));
        ValidateName(version, nameof(version));

        var directory = Path.Combine(RootDirectory, modelId, version);

        // Belt and braces: the names can't contain separators, but never touch anything outside the store.
        if (!Path.GetFullPath(directory).StartsWith(RootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The model folder would be outside the store.", nameof(modelId));
        }

        return directory;
    }

    private string GetUsageKey(string modelId, string version)
    {
        _ = GetModelDirectory(modelId, version);
        return $"{modelId}/{version}";
    }

    private async Task<Dictionary<string, List<string>>> ReadUsageAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(UsageFile))
        {
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }

        try
        {
            await using var stream = File.OpenRead(UsageFile);
            var usage = await JsonSerializer.DeserializeAsync<Dictionary<string, List<string>>>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            return new Dictionary<string, List<string>>(usage ?? [], StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "The model usage file {Path} is damaged; treating every model as unused", UsageFile);
            return new Dictionary<string, List<string>>(StringComparer.Ordinal);
        }
    }

    private async Task<InstalledModel?> TryReadManifestAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ManifestFileName);
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            var version = Path.GetFileName(directory);
            var modelId = Path.GetFileName(Path.GetDirectoryName(directory)!);
            if (manifest is null || manifest.ModelId != modelId || manifest.Version != version || manifest.Files is not { Count: > 0 })
            {
                logger.LogWarning("The model manifest {Path} doesn't match its folder", path);
                return null;
            }

            _ = ValidateFiles(manifest.Files.Select(file => new ModelFileSource(file.Role, file.FileName)).ToList());
            return manifest.ToModel(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            logger.LogWarning(ex, "Skipping the model folder {Directory}: its manifest can't be read", directory);
            return null;
        }
    }

    private void CleanUpLeftovers()
    {
        var trash = Path.Combine(RootDirectory, TrashFolderName);
        if (Directory.Exists(trash))
        {
            foreach (var directory in Directory.EnumerateDirectories(trash))
            {
                TryDeleteDirectory(directory);
            }
        }

        var staging = Path.Combine(RootDirectory, StagingFolderName);
        if (Directory.Exists(staging))
        {
            var cutoff = timeProvider.GetUtcNow() - AbandonedStagingAge;
            foreach (var directory in Directory.EnumerateDirectories(staging).Where(directory => Directory.GetLastWriteTimeUtc(directory) < cutoff.UtcDateTime))
            {
                TryDeleteDirectory(directory);
            }
        }
    }

    private void TryDeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't delete {Directory}; it will be removed later", directory);
        }
    }

    /// <summary>
    /// The <c>manifest.json</c> format.
    /// </summary>
    private sealed record Manifest(
        int Format,
        string ModelId,
        string Version,
        string DisplayName,
        string? License,
        string? Source,
        IReadOnlyList<InstalledModelFile> Files,
        string ContentHash,
        DateTimeOffset InstalledAt)
    {
        public static Manifest From(InstalledModel model) =>
            new(1, model.ModelId, model.Version, model.DisplayName, model.License, model.Source, model.Files, model.ContentHash, model.InstalledAt);

        public InstalledModel ToModel(string directory) =>
            new(ModelId, Version, DisplayName, License, Source, directory, Files, ContentHash, InstalledAt);
    }
}
