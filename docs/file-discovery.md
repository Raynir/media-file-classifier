# File discovery

How the app finds the media in an indexed folder (E03-S02). The service is `IFolderDiscovery` in `Infrastructure.Abstractions/Discovery`, implemented by `FolderDiscovery` in `src/MediaFileClassifier.Infrastructure/Discovery`. It's the first step of an import (MFC-69) and of a rescan (E02-S04).

## What a scan does

`DiscoverAsync(library, folder)` walks the folder through `IMediaFileSource.ScanAsync`, so the same code serves desktop paths and Android SAF folders. For each file it decides, in this order, without reading the file until the last step:

1. **Folder rules.** Hidden and system files and folders are skipped unless `FolderRules.IncludeHidden` is on. Subfolders are skipped when `IncludeSubfolders` is off. Include and exclude patterns are matched by `FolderRuleMatcher` (in `Domain/Libraries`, so the use cases apply the same rules to stored items); an excluded subfolder is pruned without being listed. The pattern syntax is described on `FolderRules`; a leading `/` anchors a pattern at the folder root, which is how excluded subfolders are stored (`docs/indexed-folders.md`).
2. **Extension.** Only files with a registered extension go on (`IMediaFormatRegistry.IsCandidate`).
3. **Already in the library.** The folder's items are loaded once per scan as path, size and modification time (`IMediaItemRepository.GetFileStatesAsync`). A file at a known path is never read: it counts as *unchanged* when size and time match to the stored millisecond, and as *changed* otherwise. Updating changed files is the rescan's job (E02-S04).
4. **Cloud placeholders.** A file whose content isn't stored locally (see below) is skipped and counted, unless `FolderRules.IncludeCloudPlaceholders` is on.
5. **Content.** The file is opened and `IMediaFormatRegistry.DetectAsync` reads its first bytes. The content decides the format (E03-S01); a file whose content isn't supported is counted and skipped.

New files become `MediaItem`s with status Discovered. The quick fingerprint isn't computed here; the import pipeline adds it (`docs/import-pipeline.md`), and gets each written batch through the `registered` callback.

## Links, unreadable folders and errors

- **Links:** symbolic links and junctions to folders are never followed, so a link loop can't happen. Each one is logged and listed in `DiscoveryResult.SkippedLinks`. Cloud-sync folders such as OneDrive are reparse points too; only reparse points with a link target are skipped. Links to files are read like files.
- **Unreadable folders:** a folder that can't be listed (permission denied, or deleted during the scan) is logged as a warning, listed in `DiscoveryResult.UnreadableFolders`, and the scan goes on.
- **Unreadable files:** a file that can't be opened (locked, deleted since it was listed) is logged and counted in `UnreadableFiles`.
- **Missing folder:** if the folder itself doesn't exist, `DiscoverAsync` throws `DirectoryNotFoundException`, so the caller can mark it offline. Imports check the folder first (`docs/indexed-folders.md`).

## Registration and progress

- Items are written through the library's serialized writer (D072) in batches of 100, the writer's own batch size (see "Write concurrency" in `library-storage.md`). A partial batch is also written once its first item has waited a second, so items show up while a slow folder is scanned. The wait is checked as each entry is scanned.
- `IProgress<DiscoveryProgress>` receives the folders scanned, the media files found and the items registered after each listed folder and each written batch.
- Cancelling stops the scan; batches already written stay written. The next scan skips them as unchanged.

## Partial folders

A folder in "selected files only" mode (`IndexedFolder.SelectedFilesOnly`, D012, E03-S05) isn't listed. Discovery checks that its root can be reached (`IsAvailableAsync`, so an unreachable one is Offline), then looks up only the files it tracks, its items, with `GetStatAsync`. Other files in the folder are ignored, so a large Downloads folder costs nothing, and its rules don't apply. A tracked file that can't be looked up goes into `DiscoveryResult.UncheckedFiles` and is left alone, like the files of an unreadable folder.

## Cloud placeholders

OneDrive Files On-Demand and other cloud-files providers leave placeholder files whose content is downloaded when they're read. `FileSystemMediaFileSource` marks a file as `MediaFileStat.IsCloudPlaceholder` when the directory listing shows `FILE_ATTRIBUTE_OFFLINE`, `FILE_ATTRIBUTE_RECALL_ON_OPEN` (0x40000) or `FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS` (0x400000). The attributes come from the listing, so the file is never opened. Files kept on the device ("Always keep on this device", or downloaded) don't have them, and are imported normally.

Checked on 2026-11-02 against a real OneDrive folder (2,778 files, the OneDrive client not running): the 11 online-only files were all flagged, and no file's attributes changed during the scan, so nothing was recalled. A check with the client running and network monitoring is still to do.

## Benchmark

`DiscoveryBenchmarkTests` measures discovery on generated trees of 10k and 50k small JPEG files, 100 to a folder. It runs only when `MFC_DISCOVERY_BENCHMARK_DIR` names a folder on the disk to measure:

```powershell
$env:MFC_DISCOVERY_BENCHMARK_DIR = "D:\bench"
dotnet test --project tests/MediaFileClassifier.Infrastructure.Tests -- --filter-class "*DiscoveryBenchmarkTests"
```

It times a scan alone, a first discovery that registers every file, and a rescan that finds them all unchanged. The library database is in the temp folder.

Measured on 2026-11-02 on a Windows 11 desktop with two NVMe SSDs (no HDD was available), Debug build, Microsoft Defender on. Each disk had two runs: the first right after the tree was written, the second on the same tree. Times are in seconds.

| Disk | Files | Scan only | First discovery | Files/s | Rescan, all unchanged |
|------|------:|----------:|----------------:|--------:|----------------------:|
| Samsung 990 PRO (D:) | 10,000 | 0.01 | 1.08–1.20 | 8,300–9,300 | 0.02–0.03 |
| Samsung 990 PRO (D:) | 50,000 | 0.04–0.05 | 7.35–7.57 | 6,600–6,800 | 0.14–0.15 |
| UMIS RPJYJ1T24 (C:) | 10,000 | 0.01–0.02 | 0.98–0.99 | 10,100–10,200 | 0.03 |
| UMIS RPJYJ1T24 (C:) | 50,000 | 0.05–0.08 | 7.08–8.32 | 6,000–7,100 | 0.15 |

- **Where the time goes:** listing is about 1 µs a file, so a first discovery spends its time opening each new file to detect its format and writing the items: about 140 µs a file. A rescan of an unchanged folder opens no file and reads no metadata, and takes 0.15 s for 50k files.
- **Limits of the measurement:** the trees were in the OS file cache, because they were written just before (a cold-cache run needs a reboot or admin rights), and the files are 4 KB. On an HDD or a network share each open costs a seek, so a first discovery of 50k files there could take minutes; reading headers in parallel would help there. No bottleneck stands out on SSDs, so no task was filed.
