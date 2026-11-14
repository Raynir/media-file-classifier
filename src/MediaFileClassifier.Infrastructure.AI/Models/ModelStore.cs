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
/// Layout: <c>models\&lt;id&gt;\&lt;version&gt;\&lt;tier&gt;\</c> holds the files and <c>manifest.json</c>, so tiers of
/// a version sit side by side (E06-S08-T03); <c>models\usage.json</c> maps each tier to the libraries that use it;
/// <c>models\.staging</c> and <c>models\.trash</c> hold imports in progress and deletions in progress.
/// </para>
/// <para>
/// Migration: a folder from before tiers (<c>&lt;id&gt;\&lt;version&gt;\manifest.json</c>) is moved down into its tier
/// folder on first access, by two renames through <c>&lt;id&gt;\.migrating-&lt;version&gt;</c>, so a crash between them
/// is finished on the next access. Its tier is the catalogue tier whose files it has, else the catalogue's default.
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
    private const string MigratingPrefix = ".migrating-";
    private const string FallbackTierId = "standard";
    private const int ManifestFormat = 2;
    private const int MaxNameLength = 100;
    private const int BufferSize = 1 << 20;

    private static readonly TimeSpan AbandonedStagingAge = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private readonly TimeProvider timeProvider;
    private readonly IModelCatalog? catalog;
    private readonly ILogger logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelStore"/> class in the app's data folder.
    /// </summary>
    /// <param name="paths">App folders.</param>
    /// <param name="timeProvider">Clock for installation times.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="catalog">The model catalogue, to tell the tier of a folder from before tiers.</param>
    public ModelStore(IAppPaths paths, TimeProvider timeProvider, ILogger<ModelStore>? logger = null, IModelCatalog? catalog = null)
        : this(Path.Combine(paths?.DataDirectory ?? throw new ArgumentNullException(nameof(paths)), "models"), timeProvider, logger, catalog)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ModelStore"/> class in a given folder.
    /// </summary>
    /// <param name="rootDirectory">The store's root folder.</param>
    /// <param name="timeProvider">Clock for installation times.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="catalog">The model catalogue, to tell the tier of a folder from before tiers.</param>
    internal ModelStore(string rootDirectory, TimeProvider timeProvider, ILogger? logger = null, IModelCatalog? catalog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        RootDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDirectory));
        this.timeProvider = timeProvider;
        this.catalog = catalog;
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
        foreach (var modelDirectory in Directory.EnumerateDirectories(RootDirectory).Where(IsModelName).Order(StringComparer.Ordinal).ToList())
        {
            await MigrateLegacyAsync(modelDirectory, cancellationToken).ConfigureAwait(false);
            foreach (var versionDirectory in Directory.EnumerateDirectories(modelDirectory).Where(IsModelName).Order(StringComparer.Ordinal))
            {
                models.AddRange(await ReadTiersAsync(versionDirectory, cancellationToken).ConfigureAwait(false));
            }
        }

        return models;
    }

    /// <inheritdoc />
    public async Task<InstalledModel?> FindAsync(string modelId, string version, string tierId, CancellationToken cancellationToken)
    {
        _ = GetModelDirectory(modelId, version, tierId);
        var tiers = await FindTiersAsync(modelId, version, cancellationToken).ConfigureAwait(false);
        return tiers.FirstOrDefault(model => model.TierId == tierId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InstalledModel>> FindTiersAsync(string modelId, string version, CancellationToken cancellationToken)
    {
        var versionDirectory = GetVersionDirectory(modelId, version);
        await MigrateLegacyAsync(GetModelFolder(modelId), cancellationToken).ConfigureAwait(false);
        return Directory.Exists(versionDirectory) ? await ReadTiersAsync(versionDirectory, cancellationToken).ConfigureAwait(false) : [];
    }

    /// <inheritdoc />
    public event EventHandler? ModelsChanged;

    /// <inheritdoc />
    public async Task<InstalledModel> ImportAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        var model = await ImportCoreAsync(request, progress, cancellationToken).ConfigureAwait(false);
        RaiseModelsChanged();
        return model;
    }

    private async Task<InstalledModel> ImportCoreAsync(ModelImportRequest request, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var target = GetModelDirectory(request.ModelId, request.Version, request.TierId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        var fileNames = ValidateFiles(request.Files);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyCoreAsync(GetModelFolder(request.ModelId)).ConfigureAwait(false);
            EnsureNotLegacy(request.ModelId, request.Version);
            if (Directory.Exists(target))
            {
                throw new ModelStoreException(
                    $"{request.DisplayName} {request.Version} ({request.TierId}) is already installed. Delete it first to replace it.");
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
                    request.TierId,
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
                logger.LogInformation("Installed model {ModelId} {Version} tier {TierId} ({FileCount} files)", model.ModelId, model.Version, model.TierId, files.Count);
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
    public async Task<IReadOnlyList<string>> VerifyAsync(string modelId, string version, string tierId, CancellationToken cancellationToken)
    {
        var model = await FindAsync(modelId, version, tierId, cancellationToken).ConfigureAwait(false)
            ?? throw new ModelStoreException($"The model {modelId} {version} ({tierId}) isn't installed.");

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
    public async Task RecordUsageAsync(string modelId, string version, string contentHash, string libraryPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryPath);
        var tiers = await FindTiersAsync(modelId, version, cancellationToken).ConfigureAwait(false);
        var tier = tiers.FirstOrDefault(model => string.Equals(model.ContentHash, contentHash, StringComparison.OrdinalIgnoreCase))
            ?? throw new ModelStoreException($"No installed tier of {modelId} {version} has the content hash {contentHash}.");
        var key = GetUsageKey(modelId, version, tier.TierId);
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
    public async Task<IReadOnlyList<string>> GetUsingLibrariesAsync(string modelId, string version, string tierId, CancellationToken cancellationToken)
    {
        var key = GetUsageKey(modelId, version, tierId);
        var usage = await ReadUsageAsync(cancellationToken).ConfigureAwait(false);
        return GetLibraries(usage, key, GetLegacyUsageKey(modelId, version));
    }

    /// <inheritdoc />
    public async Task<ModelDeletionResult> DeleteAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken)
    {
        var result = await DeleteCoreAsync(modelId, version, tierId, confirmed, cancellationToken).ConfigureAwait(false);
        if (result.Deleted)
        {
            RaiseModelsChanged();
        }

        return result;
    }

    private async Task<ModelDeletionResult> DeleteCoreAsync(string modelId, string version, string tierId, bool confirmed, CancellationToken cancellationToken)
    {
        var target = GetModelDirectory(modelId, version, tierId);
        var key = GetUsageKey(modelId, version, tierId);
        var legacyKey = GetLegacyUsageKey(modelId, version);

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyCoreAsync(GetModelFolder(modelId)).ConfigureAwait(false);
            EnsureNotLegacy(modelId, version);
            if (!Directory.Exists(target))
            {
                throw new ModelStoreException($"The model {modelId} {version} ({tierId}) isn't installed.");
            }

            var usage = await ReadUsageAsync(cancellationToken).ConfigureAwait(false);
            var libraries = GetLibraries(usage, key, legacyKey);
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
                throw new ModelStoreException($"The model {modelId} {version} ({tierId}) is in use. Stop the analysis that uses it, then try again.", ex);
            }

            // The folders above go once they're empty; the usage from before tiers goes with the last tier.
            var versionDirectory = Path.GetDirectoryName(target)!;
            var lastTier = !Directory.EnumerateFileSystemEntries(versionDirectory).Any();
            if (usage.Remove(key) | (lastTier && usage.Remove(legacyKey)))
            {
                await WriteJsonAtomicallyAsync(UsageFile, usage, cancellationToken).ConfigureAwait(false);
            }

            if (lastTier)
            {
                Directory.Delete(versionDirectory);
                var modelDirectory = Path.GetDirectoryName(versionDirectory)!;
                if (!Directory.EnumerateFileSystemEntries(modelDirectory).Any())
                {
                    Directory.Delete(modelDirectory);
                }
            }

            TryDeleteDirectory(trash);
            logger.LogInformation("Deleted model {ModelId} {Version} tier {TierId}", modelId, version, tierId);
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
    /// Tells the subscribers the store changed. The change is done, so a failing subscriber is logged, not thrown.
    /// </summary>
    private void RaiseModelsChanged()
    {
        try
        {
            ModelsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "A subscriber to the model store's changes failed");
        }
    }

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

    private static bool IsValidName(string value) => value.Length <= MaxNameLength && NamePattern().IsMatch(value);

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

    private static List<string> GetLibraries(Dictionary<string, List<string>> usage, string key, string legacyKey)
    {
        var libraries = usage.TryGetValue(key, out var existing) ? new List<string>(existing) : [];

        // A usage recorded before tiers that the migration couldn't rewrite: it may be this tier's, so warn about it.
        if (usage.TryGetValue(legacyKey, out var legacy))
        {
            libraries.AddRange(legacy.Where(library => !libraries.Contains(library, PathComparer)));
        }

        return libraries;
    }

    private string GetModelFolder(string modelId)
    {
        ValidateName(modelId, nameof(modelId));
        return EnsureInsideStore(Path.Combine(RootDirectory, modelId));
    }

    private string GetVersionDirectory(string modelId, string version)
    {
        ValidateName(version, nameof(version));
        return EnsureInsideStore(Path.Combine(GetModelFolder(modelId), version));
    }

    private string GetModelDirectory(string modelId, string version, string tierId)
    {
        ValidateName(tierId, nameof(tierId));
        return EnsureInsideStore(Path.Combine(GetVersionDirectory(modelId, version), tierId));
    }

    private string EnsureInsideStore(string directory)
    {
        // Belt and braces: the names can't contain separators, but never touch anything outside the store.
        if (!Path.GetFullPath(directory).StartsWith(RootDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The model folder would be outside the store.", nameof(directory));
        }

        return directory;
    }

    private string GetUsageKey(string modelId, string version, string tierId)
    {
        _ = GetModelDirectory(modelId, version, tierId);
        return $"{modelId}/{version}/{tierId}";
    }

    private string GetLegacyUsageKey(string modelId, string version)
    {
        _ = GetVersionDirectory(modelId, version);
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

    private async Task<List<InstalledModel>> ReadTiersAsync(string versionDirectory, CancellationToken cancellationToken)
    {
        var tiers = new List<InstalledModel>();

        // A folder from before tiers that couldn't be moved yet (a file is open) still works where it is.
        if (File.Exists(Path.Combine(versionDirectory, ManifestFileName))
            && await TryReadManifestFileAsync(versionDirectory, Path.GetFileName(Path.GetDirectoryName(versionDirectory)!), Path.GetFileName(versionDirectory), cancellationToken)
                .ConfigureAwait(false) is { } legacy)
        {
            tiers.Add(legacy.ToModel(versionDirectory, legacy.TierId ?? InferTier(legacy)));
        }

        foreach (var tierDirectory in Directory.EnumerateDirectories(versionDirectory).Where(IsModelName).Order(StringComparer.Ordinal))
        {
            if (await TryReadManifestAsync(tierDirectory, cancellationToken).ConfigureAwait(false) is { } model)
            {
                tiers.Add(model);
            }
        }

        return tiers;
    }

    private async Task<InstalledModel?> TryReadManifestAsync(string directory, CancellationToken cancellationToken)
    {
        var tierId = Path.GetFileName(directory);
        var versionDirectory = Path.GetDirectoryName(directory)!;
        var manifest = await TryReadManifestFileAsync(
            directory,
            Path.GetFileName(Path.GetDirectoryName(versionDirectory)!),
            Path.GetFileName(versionDirectory),
            cancellationToken).ConfigureAwait(false);
        if (manifest is null)
        {
            return null;
        }

        // A manifest moved down from before tiers, whose rewrite failed, has no tier: the folder names it.
        if (manifest.TierId is { } recorded && recorded != tierId)
        {
            logger.LogWarning("The model manifest in {Directory} names the tier {TierId}", directory, recorded);
            return null;
        }

        return manifest.ToModel(directory, tierId);
    }

    /// <summary>
    /// Reads a manifest and checks it describes the given model and version, with valid files.
    /// </summary>
    private async Task<Manifest?> TryReadManifestFileAsync(string directory, string modelId, string version, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ManifestFileName);
        try
        {
            await using var stream = File.OpenRead(path);
            var manifest = await JsonSerializer.DeserializeAsync<Manifest>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
            if (manifest is null || manifest.ModelId != modelId || manifest.Version != version || manifest.Files is not { Count: > 0 }
                || (manifest.TierId is { } tierId && !IsValidName(tierId)))
            {
                logger.LogWarning("The model manifest {Path} doesn't match its folder", path);
                return null;
            }

            _ = ValidateFiles(manifest.Files.Select(file => new ModelFileSource(file.Role, file.FileName)).ToList());
            return manifest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            logger.LogWarning(ex, "Skipping the model folder {Directory}: its manifest can't be read", directory);
            return null;
        }
    }

    /// <summary>
    /// Moves the model's folders from before tiers down into their tier folders, if it has any. Takes the lock only
    /// when there's something to move, so reads stay lock-free.
    /// </summary>
    private async Task MigrateLegacyAsync(string modelDirectory, CancellationToken cancellationToken)
    {
        if (!HasLegacyFolders(modelDirectory))
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await MigrateLegacyCoreAsync(modelDirectory).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool HasLegacyFolders(string modelDirectory) =>
        Directory.Exists(modelDirectory)
        && (Directory.EnumerateDirectories(modelDirectory, MigratingPrefix + "*").Any()
            || Directory.EnumerateDirectories(modelDirectory).Where(IsModelName).Any(directory => File.Exists(Path.Combine(directory, ManifestFileName))));

    /// <summary>
    /// Moves each <c>&lt;version&gt;</c> folder that holds a manifest to <c>&lt;version&gt;\&lt;tier&gt;</c>: first it's
    /// renamed to <c>.migrating-&lt;version&gt;</c>, then into the new tier folder. Both are single renames, and a
    /// <c>.migrating-</c> folder left by a crash is finished first. Runs under the lock; never throws for a folder
    /// it can't move (e.g. a file is open), which is logged and tried again on the next access.
    /// </summary>
    private async Task MigrateLegacyCoreAsync(string modelDirectory)
    {
        if (!HasLegacyFolders(modelDirectory))
        {
            return;
        }

        var modelId = Path.GetFileName(modelDirectory);
        foreach (var pending in Directory.EnumerateDirectories(modelDirectory, MigratingPrefix + "*").ToList())
        {
            var version = Path.GetFileName(pending)[MigratingPrefix.Length..];
            if (IsValidName(version))
            {
                await FinishMigrationAsync(modelId, version, pending).ConfigureAwait(false);
            }
        }

        foreach (var versionDirectory in Directory.EnumerateDirectories(modelDirectory).Where(IsModelName).ToList())
        {
            if (!File.Exists(Path.Combine(versionDirectory, ManifestFileName)))
            {
                continue;
            }

            var version = Path.GetFileName(versionDirectory);
            if (await TryReadManifestFileAsync(versionDirectory, modelId, version, CancellationToken.None).ConfigureAwait(false) is null)
            {
                // Unreadable before tiers too; leave it for the user to delete.
                continue;
            }

            var pending = Path.Combine(modelDirectory, MigratingPrefix + version);
            try
            {
                Directory.Move(versionDirectory, pending);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Couldn't move the model folder {Directory} into its tier folder yet; will try again", versionDirectory);
                continue;
            }

            await FinishMigrationAsync(modelId, version, pending).ConfigureAwait(false);
        }
    }

    private async Task FinishMigrationAsync(string modelId, string version, string pending)
    {
        var manifest = await TryReadManifestFileAsync(pending, modelId, version, CancellationToken.None).ConfigureAwait(false);
        if (manifest is null)
        {
            SetAside(pending, "its manifest can't be read");
            return;
        }

        var tierId = manifest.TierId ?? InferTier(manifest);
        var versionDirectory = Path.Combine(Path.GetDirectoryName(pending)!, version);
        var target = Path.Combine(versionDirectory, tierId);
        try
        {
            if (Directory.Exists(target) || File.Exists(Path.Combine(versionDirectory, ManifestFileName)))
            {
                SetAside(pending, $"{target} is taken");
                return;
            }

            Directory.CreateDirectory(versionDirectory);
            Directory.Move(pending, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't move {Pending} into {Target} yet; will try again", pending, target);
            return;
        }

        try
        {
            // The folder names the tier already; recording it too keeps the manifest self-describing.
            await WriteJsonAtomicallyAsync(Path.Combine(target, ManifestFileName), manifest with { Format = ManifestFormat, TierId = tierId }, CancellationToken.None)
                .ConfigureAwait(false);
            var usage = await ReadUsageAsync(CancellationToken.None).ConfigureAwait(false);
            if (usage.Remove(GetLegacyUsageKey(modelId, version), out var libraries))
            {
                var key = GetUsageKey(modelId, version, tierId);
                var merged = usage.TryGetValue(key, out var existing) ? existing : usage[key] = [];
                merged.AddRange(libraries.Where(library => !merged.Contains(library, PathComparer)));
                await WriteJsonAtomicallyAsync(UsageFile, usage, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Moved the model {ModelId} {Version} into the tier {TierId}, but couldn't update its manifest or usage", modelId, version, tierId);
        }

        logger.LogInformation("Moved the model {ModelId} {Version} into its tier folder {TierId}", modelId, version, tierId);
    }

    /// <summary>
    /// Renames a <c>.migrating-</c> folder that can never be finished to <c>.unmigrated-&lt;version&gt;-&lt;id&gt;</c>, so it
    /// no longer blocks import and delete of its version as "being moved". Nothing is deleted: the files stay for the
    /// user to look at or remove.
    /// </summary>
    private void SetAside(string pending, string reason)
    {
        var aside = Path.Combine(
            Path.GetDirectoryName(pending)!,
            ".unmigrated-" + Path.GetFileName(pending)[MigratingPrefix.Length..] + "-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.Move(pending, aside);
            logger.LogWarning("Couldn't move {Pending} into its tier folder ({Reason}); left it as {Aside} for the user to check", pending, reason, aside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Couldn't move {Pending} into its tier folder ({Reason}), nor set it aside; will try again", pending, reason);
        }
    }

    /// <summary>
    /// Tells the tier of a manifest from before tiers: the catalogue tier with the same files, else the default tier.
    /// </summary>
    private string InferTier(Manifest manifest)
    {
        if (catalog?.Find(manifest.ModelId) is not { } entry || entry.Version != manifest.Version)
        {
            return FallbackTierId;
        }

        var contentHash = ComputeContentHash(manifest.Files);
        var match = entry.Tiers.FirstOrDefault(
            tier => ComputeContentHash(tier.Files.Select(file => new InstalledModelFile(file.Role, file.FileName, file.Size, file.Sha256))) == contentHash);
        return match?.TierId ?? entry.DefaultTier;
    }

    /// <summary>
    /// Fails an operation on a version whose folder from before tiers couldn't be moved yet, so nothing is written
    /// next to its files.
    /// </summary>
    private void EnsureNotLegacy(string modelId, string version)
    {
        if (File.Exists(Path.Combine(GetVersionDirectory(modelId, version), ManifestFileName))
            || Directory.Exists(Path.Combine(GetModelFolder(modelId), MigratingPrefix + version)))
        {
            throw new ModelStoreException($"The model {modelId} {version} is being moved to the new folder layout and is in use. Try again later.");
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
        DateTimeOffset InstalledAt,
        string? TierId = null)
    {
        public static Manifest From(InstalledModel model) =>
            new(ManifestFormat, model.ModelId, model.Version, model.DisplayName, model.License, model.Source, model.Files, model.ContentHash, model.InstalledAt, model.TierId);

        public InstalledModel ToModel(string directory, string tierId) =>
            new(ModelId, Version, tierId, DisplayName, License, Source, directory, Files, ContentHash, InstalledAt);
    }
}
