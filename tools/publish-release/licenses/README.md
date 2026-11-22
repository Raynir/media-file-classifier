# Licence texts shipped in the release zip

`tools/publish-release/New-Release.ps1` copies these files into the zip's `licenses/` folder, next to `LICENSE` and `THIRD_PARTY_NOTICES.md` (MFC-1026).

| File | What it is | Official source |
|---|---|---|
| `LGPL-2.1.txt` | GNU Lesser General Public License, version 2.1, for LibVLC (`VideoLAN.LibVLC.Windows` 3.0.24, LGPL-2.1-or-later) | https://www.gnu.org/licenses/old-licenses/lgpl-2.1.txt |
| `DirectML-LICENSE.txt` | Microsoft software licence terms for DirectML (`Microsoft.AI.DirectML` 1.15.4) | `LICENSE.txt` inside the NuGet package https://www.nuget.org/packages/Microsoft.AI.DirectML/1.15.4 (the package's `<license type="file">`) |
| `DirectML-ThirdPartyNotices.txt` | DirectML's own third-party notices | `ThirdPartyNotices.txt` inside the same package |

When the DirectML or LibVLC version changes, check the texts against the new package and replace them.
