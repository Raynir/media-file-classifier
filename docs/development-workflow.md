# Development workflow

The branching and commit rules (D064).

## Branches

| Branch | Purpose |
|--------|---------|
| `main` | The latest finished milestone. Changes only through release and hotfix merges. |
| `develop` | Integration branch. Feature branches start here and merge back here. |
| `feature/MFC-<key>-<short-description>` | One sub-task, e.g. `feature/MFC-222-solution-layout`. A Story whose sub-tasks are tightly coupled can use one branch for the whole Story, e.g. `feature/MFC-51-solution-structure`. |
| `release/<version>` | Staging for a release, where the final tests run. |
| `hotfix/<version>` | An urgent fix to `main`, e.g. `hotfix/1.0.1`. |

## Releases

- **MVP milestones:** at the end of each milestone, cut `release/M0` … `release/M5` from `develop`.
  1. Test the branch, and update `CHANGELOG.md`.
  2. Merge it into `main` and back into `develop`.
  3. Tag `main` as `m0` … `m4`. `release/M5` is tagged `v1.0.0` (D058).
- **After the MVP:** use `release/1.1.0`, `release/1.2.0` and so on, with a `v1.x.0` tag.
- **Hotfixes:** a hotfix branches from `main`, and merges into `main` and `develop`, with a `v1.x.y` tag.

## Commits

- Keep each commit atomic: one logical change that builds on its own.
- End the message with the Jira key of the sub-task it implements:

  ```text
  Add shared build configuration MFC-224
  ```

- The key links the commit to the issue's Development panel in Jira.

## Pull requests

- Open pull requests from `feature/*` into `develop`.
- The title names the Story or sub-task key.
- The **CI** checks must pass: *Build and test (Windows)*, *Build shared projects (Linux)* and *Build Android head*.
- A UI change runs the real-app check and attaches its key screenshots:

  ```powershell
  dotnet run --project src/MediaFileClassifier.Desktop -- --smoke tools/ui-smoke/sample.smoke --out artifacts/ui-smoke
  ```

  Exit code 0 means every step passed and no error or Avalonia warning was logged. Steps, options and output are in [testing.md](testing.md#real-app-checks-ui-smoke-tool).

## Opt-in tests

A plain `dotnet test` (and CI) skips tests that are slow, heavy or need the network or large files. Each one is switched on by an environment variable and skips with a message that names it.

| Variable | Test | Needs | Cost |
|---|---|---|---|
| `MFC_QUALITY_TESTS=1` | `Siglip2QualityTests` (SigLIP 2 embedding quality against the D094 baselines) | `models/siglip2` or `MFC_SIGLIP2_DIR`, and the fetched `eval/corpus` | About 2 minutes, all CPU cores, so it makes timing tests fail if it runs alongside them |
| `MFC_NETWORK_TESTS=1` | `ModelCatalogTests` network checks | Network access | A few requests |

Run one on its own, never with the whole suite:

```powershell
$env:MFC_QUALITY_TESTS = "1"
dotnet test --project tests/MediaFileClassifier.Evaluation.Tests -- --filter-class "*Siglip2QualityTests"
```

## Android development

The Android app is `src/MediaFileClassifier.App.Android` (E20). It needs the .NET **android workload**, the **Android SDK** and a **JDK**, and none of these are needed for the desktop app. The solution marks the head `Build Project="false"` (D080), so `dotnet build` and `dotnet test` at the repository root skip it, and work without any of this. CI builds the head in its own job, *Build Android head*.

All commands below are PowerShell, run from the repository root. Don't run them from Git Bash: it rewrites device paths such as `/sdcard/...` into Windows paths.

### Install the toolchain

Choose one route.

**Visual Studio route.**

1. In the Visual Studio Installer, add the **.NET Multi-platform App UI development** workload.
2. That installs the android workload, the Android SDK (`C:\Program Files (x86)\Android\android-sdk`) and Microsoft OpenJDK (`C:\Program Files\Android\openjdk\jdk-21.*`), and registers both locations for `dotnet build`.

**Command-line route.**

1. From an **elevated** terminal, install the workload:

   ```powershell
   dotnet workload install android
   ```

2. Install the SDK and the JDK that the project needs, to folders without spaces:

   ```powershell
   dotnet build src/MediaFileClassifier.App.Android -t:InstallAndroidDependencies -p:AndroidSdkDirectory=C:\android-sdk -p:JavaSdkDirectory=C:\jdk -p:AcceptAndroidSdkLicenses=True
   ```

3. Tell later builds where they are. You can pass the same `-p:AndroidSdkDirectory=...` and `-p:JavaSdkDirectory=...` to every build, or set the variables once:

   ```powershell
   [Environment]::SetEnvironmentVariable("ANDROID_HOME", "C:\android-sdk", "User")
   [Environment]::SetEnvironmentVariable("JAVA_HOME", "C:\jdk", "User")
   ```

   Open a new terminal afterwards. If Visual Studio's Android tools are also installed, the JDK they registered wins over `JAVA_HOME` (`ANDROID_HOME` still works), so pass `-p:JavaSdkDirectory=...` to use another JDK.

To check either route:

```powershell
dotnet workload list
dotnet build src/MediaFileClassifier.App.Android
```

The first command should list `android`. The second should end with `Build succeeded.` and 0 warnings.

`adb` is in the SDK's `platform-tools` folder. The commands below keep its path in a variable. Change it if your SDK is elsewhere:

```powershell
$adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
```

### Prepare a phone

1. **Turn on developer options.** On the phone, open **Settings › About phone › Software information** and tap **Build number** seven times. On other brands than Samsung, it's **Settings › About phone**.
2. **Turn on USB debugging** in **Settings › Developer options**.
3. **Connect the phone over USB**, and accept the **Allow USB debugging?** prompt for this computer.
4. **Check the connection:**

   ```powershell
   & $adb devices
   ```

   The phone should be listed as `device`. `unauthorized` means the prompt wasn't accepted.

The head targets arm64 phones on Android 8.0 (API 26) or later.

### Build, deploy and run

**Debug.** This builds, installs and starts the app in one step, taking about a minute and a half:

```powershell
dotnet build src/MediaFileClassifier.App.Android -t:Run
```

**Release.** This is AOT-compiled with LLVM (D081), takes about 4 minutes, and is the build to measure with:

```powershell
dotnet publish src/MediaFileClassifier.App.Android -c Release -o artifacts\android
& $adb install -r artifacts\android\com.raynir.mediafileclassifier-Signed.apk
& $adb shell monkey -p com.raynir.mediafileclassifier -c android.intent.category.LAUNCHER 1
```

The last command starts the app. `artifacts/` is git-ignored.

Both builds are signed with this machine's debug key; release signing comes with E20-S09. If you have more than one device connected, pick one with `-p:AdbTarget="-s <serial>"` on `dotnet build`, and with `-s <serial>` on `adb`.

To remove the app and its data:

```powershell
& $adb uninstall com.raynir.mediafileclassifier
```

### Read logs

The head doesn't write log files yet (MFC-979). For now, use logcat:

```powershell
& $adb logcat -s MfcSpike                      # the smoke-test page's results
& $adb logcat AndroidRuntime:E DOTNET:E *:S    # crashes and .NET runtime errors
& $adb logcat -b crash                         # the crash buffer
```

Press Ctrl+C to stop. Add `-d` to print what's there and exit, and run `& $adb logcat -c` first to clear old lines.

### The smoke-test page

The head opens on the MFC-18 smoke-test page: buttons for SQLite, folder access (SAF), vector search and ONNX Runtime. Its results go to the page and to the `MfcSpike` logcat tag. You can also start a script of checks from the command line:

```powershell
$activity = & $adb shell cmd package resolve-activity --brief com.raynir.mediafileclassifier | Select-Object -Last 1
& $adb shell am start -n $activity --es spike "'sqlite;vectors:100000'"
```

Keep the single quotes inside the double quotes: `adb shell` hands the line to the phone's shell, which would otherwise split it at the `;`.

The checks are `sqlite`, `saf`, `vectors:<count>` and `onnx:<model>:<provider>`. The model is `e5` or `siglip2`, and the provider is `cpu`, `xnnpack` or `nnapi`.

The ONNX checks need the model files on the phone. See [the smoke test findings](spikes/android-smoke-test.md) for the files. Copy them as follows:

1. Start the app once, so that Android creates its folder.
2. Push the files:

   ```powershell
   $models = "/sdcard/Android/data/com.raynir.mediafileclassifier/files/models"
   & $adb shell mkdir -p "$models/e5" "$models/siglip2"
   & $adb push model_int8.onnx sentencepiece.bpe.model "$models/e5/"
   & $adb push text_model_int8.onnx tokenizer.model "$models/siglip2/"
   & $adb shell chmod -R a+rwX $models
   ```

The `chmod` is needed because folders that `adb` creates belong to the shell user. Without it, the app can't open them, and ONNX Runtime fails with `system error number 13`.

### Troubleshooting

- **`NETSDK1147: To build this project, the following workloads must be installed: wasm-tools`**, even for a plain `dotnet new android` project.
  - **Cause:** an SDK update installed workload manifests whose packs are missing. In MFC-18, the mono toolchain needed `Microsoft.NET.Runtime.MonoTargets.Sdk` 10.0.14 and only older versions were installed.
  - **Fix:** run `dotnet workload update` from an **elevated** terminal, then `dotnet workload install android`. Wait for the installer to finish before retrying. With the Visual Studio route, updating Visual Studio in its installer does the same.
- **`INSTALL_FAILED_UPDATE_INCOMPATIBLE`:** the installed app was signed with another machine's debug key. Uninstall it first.
- **`adb: no devices/emulators found`, or the device shows as `unauthorized`:** reconnect the cable, and accept the USB debugging prompt on the phone.
- **`Shell does not have permission to access user 150`** (or another number) after some `adb shell` commands: Samsung's Secure Folder is a second user. The message is harmless.
