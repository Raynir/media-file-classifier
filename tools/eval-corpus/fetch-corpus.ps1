<#
.SYNOPSIS
    Fetches the public evaluation corpus (E01-S10) into eval/corpus.

.DESCRIPTION
    Downloads the zip named in eval/corpus-release.json from the repository's GitHub release with the
    GitHub CLI (the repository is private, so run `gh auth login` once), checks its SHA-256, unpacks it,
    and checks every file against eval/manifest.json. Running it again does nothing when the corpus is
    already in place.

.EXAMPLE
    pwsh tools/eval-corpus/fetch-corpus.ps1
#>
[CmdletBinding()]
param(
    # Download and unpack again even if the corpus looks complete.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$release = Get-Content (Join-Path $root 'eval/corpus-release.json') -Raw | ConvertFrom-Json
$manifest = Get-Content (Join-Path $root 'eval/manifest.json') -Raw | ConvertFrom-Json
$corpus = Join-Path $root 'eval/corpus'
$marker = Join-Path $corpus '.release'

function Test-Corpus {
    $bad = @()
    foreach ($item in $manifest) {
        $path = Join-Path $corpus $item.file
        if (-not (Test-Path -LiteralPath $path)) { $bad += "missing $($item.file)"; continue }
        if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $item.sha256) { $bad += "checksum $($item.file)" }
    }
    return , $bad
}

if (-not $Force -and (Test-Path $marker) -and ((Get-Content $marker -Raw).Trim() -eq $release.sha256)) {
    $bad = Test-Corpus
    if ($bad.Count -eq 0) {
        Write-Host "The corpus is already in eval/corpus ($($manifest.Count) files)."
        exit 0
    }
    Write-Host "The corpus is incomplete ($($bad.Count) problems); fetching it again."
}

if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'The GitHub CLI (gh) is needed: https://cli.github.com, then run `gh auth login`.'
}

$download = Join-Path ([IO.Path]::GetTempPath()) "mfc-eval-corpus-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $download | Out-Null
try {
    Write-Host "Downloading $($release.asset) ($([math]::Round($release.bytes / 1MB)) MB) from $($release.repository) release $($release.tag)..."
    gh release download $release.tag --repo $release.repository --pattern $release.asset --dir $download
    if ($LASTEXITCODE -ne 0) { throw "gh release download failed with exit code $LASTEXITCODE." }

    $zip = Join-Path $download $release.asset
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $release.sha256) { throw "Checksum mismatch: expected $($release.sha256), got $hash." }

    # Only this script's own folder is ever replaced.
    if (Test-Path $corpus) { Remove-Item -LiteralPath $corpus -Recurse -Force }
    Expand-Archive -LiteralPath $zip -DestinationPath $corpus
    Set-Content -LiteralPath $marker -Value $release.sha256
}
finally {
    Remove-Item -LiteralPath $download -Recurse -Force -ErrorAction SilentlyContinue
}

$bad = Test-Corpus
if ($bad.Count -gt 0) { throw "The unpacked corpus doesn't match eval/manifest.json:`n$($bad -join "`n")" }
Write-Host "Fetched $($manifest.Count) files into eval/corpus. Attribution: eval/corpus/ATTRIBUTION.md."
