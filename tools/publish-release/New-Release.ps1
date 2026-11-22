<#
.SYNOPSIS
    Builds the Windows release and publishes it as a GitHub Release (MFC-1023).

.DESCRIPTION
    Run from the repository root on the release's tag (docs/development-workflow.md, "Releases"). It:
      1. reads the version from <Version> in Directory.Build.props (the tag is v<version>);
      2. takes the release notes from the "## <version> — <date>" section of CHANGELOG.md;
      3. publishes the desktop app self-contained for win-x64 and zips it, with a SHA-256 file;
      4. runs "gh release create" with the notes and both files.
    GitHub Actions is not used: this works from any machine that has the .NET SDK and an authenticated gh.

    A real release needs a clean working tree, HEAD on the tag v<version>, and that tag pushed to origin.
    -Draft and -DryRun skip those checks, so the whole flow can be tried before the tag exists.

.PARAMETER Version
    Overrides the version from Directory.Build.props. Only with -Draft or -DryRun.

.PARAMETER Section
    The CHANGELOG section to take the notes from. Defaults to the version. "Unreleased" works for a trial run.

.PARAMETER Draft
    Creates a draft release (visible only to people with write access), which can be published or deleted on GitHub.
    The tag is not created until the draft is published, so --target is the current commit, which must be pushed to origin.

.PARAMETER DryRun
    Builds and zips, then prints the gh command instead of running it.

.PARAMETER SkipBuild
    Reuses the zip from an earlier run in artifacts/release/<version>. Not for a real release.

.EXAMPLE
    pwsh tools/publish-release/New-Release.ps1 -DryRun
    pwsh tools/publish-release/New-Release.ps1 -Draft -Section Unreleased -Version 0.0.0-trial
    pwsh tools/publish-release/New-Release.ps1
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Section,
    [switch] $Draft,
    [switch] $DryRun,
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..' '..'))

function Invoke-Native([string] $Command, [string[]] $Arguments) {
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

$trial = $Draft -or $DryRun

# 1. Version and tag.
$declared = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($Version -and -not $trial) {
    throw 'Use -Version only with -Draft or -DryRun: a real release takes the version from Directory.Build.props.'
}
if (-not $Version) { $Version = $declared }
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "'$Version' is not a SemVer version (1.0.0, 1.1.0-rc.1)."
}
$tag = "v$Version"
$commit = (git rev-parse HEAD).Trim()

if (-not $trial) {
    if (git status --porcelain) { throw 'The working tree is not clean.' }
    $tagCommit = (git rev-parse --verify --quiet "refs/tags/$tag^{commit}")
    if (-not $tagCommit) { throw "Tag $tag doesn't exist. Tag the release first (docs/development-workflow.md)." }
    if ($tagCommit.Trim() -ne $commit) { throw "HEAD is not at tag $tag. Check out the tag." }
    $remote = git ls-remote --tags origin "refs/tags/$tag" "refs/tags/$tag^{}"
    if (-not $remote) { throw "Tag $tag isn't on origin. Push it first." }
    if (-not ($remote | Where-Object { $_ -like "$commit*" })) { throw "Tag $tag on origin points at a different commit than HEAD." }
}

# 2. Notes from the CHANGELOG.
if (-not $Section) { $Section = $Version }
$changelog = Get-Content CHANGELOG.md -Raw
$escaped = [regex]::Escape($Section)
$match = [regex]::Match($changelog, "(?ms)^## $escaped(?: — [^\r\n]*)?\r?\n(?<body>.*?)(?=^## |\z)")
if (-not $match.Success) { throw "CHANGELOG.md has no '## $Section' section." }
$notes = $match.Groups['body'].Value.Trim()
if (-not $notes) { throw "The '## $Section' section of CHANGELOG.md is empty." }

$output = Join-Path 'artifacts' 'release' $Version
$publishDir = Join-Path $output 'publish'
$zipName = "MediaFileClassifier-$Version-win-x64.zip"
$zip = Join-Path $output $zipName
$hashFile = "$zip.sha256"
$notesFile = Join-Path $output 'notes.md'

# 3. Build and zip.
New-Item -ItemType Directory -Force $output | Out-Null
Set-Content -Path $notesFile -Value $notes -Encoding utf8
if (-not $SkipBuild) {
    Remove-Item $publishDir, $zip, $hashFile -Recurse -Force -ErrorAction SilentlyContinue
    Invoke-Native dotnet @('publish', 'src/MediaFileClassifier.Desktop', '-c', 'Release', '-r', 'win-x64', '--self-contained', "-p:Version=$Version", '-o', $publishDir, '-v', 'q')
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip -CompressionLevel Optimal
}
if (-not (Test-Path $zip)) { throw "$zip doesn't exist. Run without -SkipBuild." }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path $hashFile -Value "$hash  $zipName" -Encoding ascii
'{0}: {1:N1} MB, sha256 {2}' -f $zipName, ((Get-Item $zip).Length / 1MB), $hash | Write-Host

# 4. The release.
$ghArguments = @('release', 'create', $tag, $zip, $hashFile, '--title', "Media File Classifier $Version", '--notes-file', $notesFile)
if ($Version -match '-') { $ghArguments += '--prerelease' }
if ($Draft) { $ghArguments += @('--draft', '--target', $commit) } else { $ghArguments += '--verify-tag' }

if ($DryRun) {
    Write-Host "`nDry run. Notes ($notesFile):`n"
    Write-Host $notes
    Write-Host "`nWould run: gh $($ghArguments -join ' ')"
    return
}

Invoke-Native gh $ghArguments
Write-Host "`nCreated release $tag$(if ($Draft) { ' (draft)' })."
