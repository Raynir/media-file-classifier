# Media File Classifier

Find any photo or video on your computer by describing it.

Media File Classifier is a Windows desktop app. You point it at your folders, and it reads each file with AI models that run on your own computer. It records a description, tags, the text in the picture and a "meaning" vector. Then you search in plain words, such as "dog on a beach at sunset", or look for pictures similar to one you already have.

- **Search by description** in English or Russian, with filters and sorting, in a grid or list view.
- **Descriptions, tags and text:** each image gets a title, a description and tags, and text in images is read (OCR). You can edit them, mark favourites and review items.
- **Videos and animated images:** the app samples key frames, so a clip is searchable too.
- **Folders stay where they are.** A library is a separate folder with an index and thumbnails. A folder that's unplugged shows as offline.

## System requirements

| | |
|---|---|
| **Operating system** | Windows 11, 64-bit (x64). |
| **Processor and graphics** | Any x64 processor works. A graphics card with DirectX 12 and at least 2 GB of its own memory is used through DirectML to speed up search indexing. Without one the processor does the work, only slower. |
| **Download** | About 121 MB (`MediaFileClassifier-<version>-win-x64.zip`). |
| **Disk space** | About 340 MB for the unzipped app. The AI models are separate downloads: about 2.6 GB for the ones that are used by default (search model 1.5 GB, descriptions 1.1 GB, text reading 13 MB), 0.9 GB for the smaller graphics-card version of the search model, and 3.3 GB more for the optional larger description model. A library adds its own index and thumbnails next to your choice of location. |
| **Internet** | Only to download the models, once. After that the app works offline. |

## Download and run

1. Open the [Releases page](https://github.com/Raynir/media-file-classifier/releases) and download `MediaFileClassifier-<version>-win-x64.zip` from the latest release. The `.sha256` file next to it lets you check the download.
2. Windows marks files from the internet as blocked. Right-click the zip, choose **Properties**, tick **Unblock** and click OK. Or run `Unblock-File` on it in PowerShell.
3. Extract the zip into a folder you own, such as `C:\Apps\MediaFileClassifier`, and run `MediaFileClassifier.Desktop.exe`. There is nothing to install.

To update, extract the new version over a new folder. Your libraries and settings are kept outside the app folder.

## Your first search

1. **Create a library.** On the welcome screen choose **Create library** (or **Library, New library…**) and pick a name and a place on a disk with some free space.
2. **Add your folders.** Choose **Library, Add folder…**. The app finds the supported files in it and in its subfolders. It reads JPEG, PNG, GIF, WebP and MP4 (`.mp4`, `.m4v`) files. You can also drop files into the window or use **Library, Import files…**.
3. **Get the AI models.** Open **Tools, Settings, AI & Models**. Choose **Download** on each model you want, one time. You need at least the search model (SigLIP 2) to search by description. The others add descriptions and tags (Florence-2) and text reading (PP-OCRv5). The Standard version suits the processor, and Compact suits a graphics card.
   - **No internet on this computer?** Download the model files on another computer and use **Import from file**. It asks for the folder with the model's files and picks the ones it needs by name. The files are listed in `src/MediaFileClassifier.Infrastructure.AI/Models/model-catalog.json`.
4. **Wait for the analysis.** New items are analysed automatically. Items wait while a model is missing; after you install one, choose **Library, Analyze pending items** if they are still waiting. Progress shows in the status bar and in **View, Processing queue**. You can search while it runs: items that are done are searchable.
5. **Search.** Type a description in the search box at the top.

## Optional: a local model server for descriptions

By default the descriptions come from Florence-2, which runs inside the app. If you prefer a stronger vision model, the app can use any server with an OpenAI-compatible address, such as [LM Studio](https://lmstudio.ai/) (`http://localhost:1234/v1`) or Ollama (`http://localhost:11434/v1`):

1. Start the server with a vision-capable model loaded.
2. In **Tools, Settings, AI & Models, Endpoints**, add it (there is a shortcut for LM Studio), choose the model, and use **Test**.

When a server is set up, it takes over the descriptions. If you use a server on another computer, your images are sent to it, and the app asks you to confirm. Details are in [docs/analysis-pipeline.md](docs/analysis-pipeline.md).

## Optional: ffprobe for video

Reading a video's duration, size and codecs, and sampling its key frames for analysis, need the free [FFmpeg](https://ffmpeg.org/) tools, `ffmpeg` and `ffprobe`. The app doesn't ship them. Without them, videos are still imported, with only their file information, and are not analysed.

```powershell
winget install --id Gyan.FFmpeg.Essentials
```

The app looks in a folder named `ffmpeg` next to `MediaFileClassifier.Desktop.exe` and in every folder on `PATH`. Restart the app after you install it.

## Where things are kept

| What | Where |
|---|---|
| **Libraries** | The folder you chose, as `<name>.mfc`: the database, thumbnails and backups. Move or back up the whole folder. |
| **Settings** | `%LOCALAPPDATA%\MediaFileClassifier\settings.json`. Delete the file to reset. |
| **AI models** | `%LOCALAPPDATA%\MediaFileClassifier\models`. **Tools, Settings, AI & Models, Open models folder** opens it. |
| **Logs** | `%LOCALAPPDATA%\MediaFileClassifier\logs`, one file a day, the last 14 days. **Help, Open log folder** opens it. |

Your media files stay where they are. Deleting a library deletes only the files the app wrote into its folder, never your media.

## Privacy

Everything runs on your computer. Your files, descriptions and searches are not sent anywhere, and the app has no accounts, tracking or usage reports. The app uses the network in only two cases: to download a model from Hugging Face or ModelScope when you ask, and to talk to a model server that you add yourself. A model server on `localhost` keeps your images on the computer.

## Known limitations

- **Windows 11 on x64 only.** Other systems and Windows on ARM aren't tested, and the DirectML graphics acceleration is not available on ARM.
- **File types:** JPEG, PNG, GIF, WebP and MP4 only. Other formats (HEIC, RAW, MOV, MKV, …) are not imported.
- **Graphics card:** speed-up was checked on an NVIDIA card. AMD, Intel Arc and machines without a graphics card haven't been measured; the app falls back to the processor when the card can't be used. Integrated graphics with less than 2 GB of their own memory are not used. The built-in Florence-2 description model always runs on the processor, so descriptions are the slowest step.
- **Languages:** search was measured with English and Russian queries. The text-reading model covers Latin and Cyrillic scripts (Eastern European and Slavic languages), not Chinese, Japanese, Arabic and others.
- **Video** analysis needs `ffmpeg` and `ffprobe` (see above).
- **First analysis of a large collection takes a long time** on a processor, since each item goes through several models.
- The AI can be wrong, so descriptions and tags are suggestions: you can edit them.

## Reporting a problem

Open **Help, Open log folder**, and attach the newest `mfc-<date>.log` file to a report. **Help, About Media File Classifier** shows the version to quote. Report it on the [issues page](https://github.com/Raynir/media-file-classifier/issues), saying what you did, what you expected and what happened. The log has file names and folder paths, so look through it first.

## Licence

Media File Classifier is licensed under the [Apache License 2.0](LICENSE). The third-party libraries and AI models it uses, and their licences, are listed in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). The release zip also carries both files, and a `licenses` folder with the texts for LibVLC (LGPL-2.1) and DirectML.

## For developers

Setting up a development machine, building, testing, the solution structure and CI are in [docs/development.md](docs/development.md). Branching and commit rules are in [docs/development-workflow.md](docs/development-workflow.md). The backlog is in Jira (project MFC), and the decisions behind it are in the [development plan repository](https://github.com/Raynir/mfc-development-plan/blob/main/DECISIONS.md).
