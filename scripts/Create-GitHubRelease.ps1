[CmdletBinding()]
param(
    [string]$Version = "v1.1.0",
    [switch]$IncludeSourceArchives,
    [switch]$WriteChecksums,
    [switch]$KeepStaging,
    [switch]$DryRun,
    [switch]$Help
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Show-Usage {
    Write-Host @"
Create-GitHubRelease.ps1

Builds local GitHub release artifacts into ./github-release.

Usage:
  .\scripts\Create-GitHubRelease.ps1
  .\scripts\Create-GitHubRelease.ps1 -Version v1.1.0
  .\scripts\Create-GitHubRelease.ps1 -DryRun

Options:
  -Version <version>        Release version. Defaults to v1.1.0.
  -IncludeSourceArchives    Also create local source zip/tar.gz archives from HEAD.
                            GitHub creates the "Source code" release rows automatically from the tag.
  -WriteChecksums           Write SHA256SUMS.txt beside the archives.
  -KeepStaging              Keep intermediate publish folders under github-release/_staging.
  -DryRun                   Print actions without building or writing files.
  -Help                     Show this help.
"@
}

if ($Help) {
    Show-Usage
    exit 0
}

if ($Version -notmatch '^v?\d+\.\d+\.\d+([-.][A-Za-z0-9.-]+)?$') {
    throw "Version must look like v1.1.0 or 1.1.0. Received '$Version'."
}

$versionTag = if ($Version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) { $Version } else { "v$Version" }
$versionNumber = $versionTag.Substring(1)
$semverCore = ($versionNumber -split '-', 2)[0]
$assemblyVersion = "$semverCore.0"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$releaseRoot = Join-Path $repoRoot "github-release"
$stagingRoot = Join-Path $releaseRoot "_staging"
$projectPath = Join-Path $repoRoot "src\QuestParser.Desktop\QuestParser.Desktop.csproj"

$artifacts = @(
    @{
        Runtime = "linux-x64"
        Folder = "linux-x64"
        File = "eq2emu-questparser-linux-x64.tar.gz"
        Archive = "tar.gz"
    },
    @{
        Runtime = "osx-arm64"
        Folder = "macos-arm64"
        File = "eq2emu-questparser-macos-arm64.tar.gz"
        Archive = "tar.gz"
    },
    @{
        Runtime = "osx-x64"
        Folder = "macos-x64"
        File = "eq2emu-questparser-macos-x64.tar.gz"
        Archive = "tar.gz"
    },
    @{
        Runtime = "win-x64"
        Folder = "win-x64"
        File = "eq2emu-questparser-win-x64.zip"
        Archive = "zip"
    }
)

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message"
}

function Invoke-CheckedCommand {
    param(
        [string]$Executable,
        [string[]]$Arguments,
        [string]$Description
    )

    if ($DryRun) {
        Write-Host "[dry-run] $Executable $($Arguments -join ' ')"
        return
    }

    Write-Step $Description
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE while running: $Description"
    }
}

function Assert-ChildPath {
    param(
        [string]$ParentPath,
        [string]$ChildPath
    )

    $parentFullPath = [System.IO.Path]::GetFullPath($ParentPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $childFullPath = [System.IO.Path]::GetFullPath($ChildPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    if (-not $childFullPath.StartsWith($parentFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to operate on '$childFullPath' because it is not under '$parentFullPath'."
    }
}

function New-CleanDirectory {
    param([string]$Path)

    Assert-ChildPath -ParentPath $repoRoot -ChildPath $Path

    if ($DryRun) {
        Write-Host "[dry-run] recreate directory $Path"
        return
    }

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Compress-ReleaseFolder {
    param(
        [string]$PublishDir,
        [string]$DestinationPath,
        [string]$ArchiveKind
    )

    if ($DryRun) {
        Write-Host "[dry-run] create $DestinationPath from $PublishDir"
        return
    }

    if ($ArchiveKind -eq "zip") {
        if (Test-Path -LiteralPath $DestinationPath) {
            Remove-Item -LiteralPath $DestinationPath -Force
        }

        Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath $DestinationPath -CompressionLevel Optimal
        return
    }

    Invoke-CheckedCommand `
        -Executable "tar" `
        -Arguments @("-czf", $DestinationPath, "-C", $PublishDir, ".") `
        -Description "Create $(Split-Path -Leaf $DestinationPath)"
}

function New-SourceArchives {
    $sourcePrefix = "eq2emu-questparser-$versionNumber"
    $sourceZip = Join-Path $releaseRoot "$sourcePrefix-source.zip"
    $sourceTar = Join-Path $stagingRoot "$sourcePrefix-source.tar"
    $sourceTarGz = Join-Path $releaseRoot "$sourcePrefix-source.tar.gz"
    $sourceTemp = Join-Path $stagingRoot "source"

    Invoke-CheckedCommand `
        -Executable "git" `
        -Arguments @("-C", $repoRoot, "archive", "--format=zip", "--prefix=$sourcePrefix/", "-o", $sourceZip, "HEAD") `
        -Description "Create source zip from HEAD"

    if ($DryRun) {
        Write-Host "[dry-run] create $sourceTarGz from HEAD"
        return
    }

    New-Item -ItemType Directory -Path $sourceTemp -Force | Out-Null

    Invoke-CheckedCommand `
        -Executable "git" `
        -Arguments @("-C", $repoRoot, "archive", "--format=tar", "--prefix=$sourcePrefix/", "-o", $sourceTar, "HEAD") `
        -Description "Create temporary source tar from HEAD"

    Invoke-CheckedCommand `
        -Executable "tar" `
        -Arguments @("-xf", $sourceTar, "-C", $sourceTemp) `
        -Description "Extract temporary source archive"

    Invoke-CheckedCommand `
        -Executable "tar" `
        -Arguments @("-czf", $sourceTarGz, "-C", $sourceTemp, $sourcePrefix) `
        -Description "Create source tar.gz from HEAD"
}

function Write-HashManifest {
    $manifestPath = Join-Path $releaseRoot "SHA256SUMS.txt"

    if ($DryRun) {
        Write-Host "[dry-run] write $manifestPath"
        return
    }

    $hashLines = Get-ChildItem -LiteralPath $releaseRoot -File |
        Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
        Sort-Object Name |
        ForEach-Object {
            $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName
            "$($hash.Hash.ToLowerInvariant())  $($_.Name)"
        }

    Set-Content -LiteralPath $manifestPath -Value $hashLines -Encoding ASCII
}

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Desktop project was not found at '$projectPath'."
}

Write-Step "Preparing GitHub release artifacts for $versionTag"
Write-Host "Repository: $repoRoot"
Write-Host "Output:     $releaseRoot"

$status = & git -C $repoRoot status --porcelain
if ($LASTEXITCODE -eq 0 -and $status) {
    if ($IncludeSourceArchives) {
        Write-Warning "The working tree has uncommitted changes. Publish artifacts use the working tree; source archives are created from HEAD."
    }
    else {
        Write-Warning "The working tree has uncommitted changes. Publish artifacts use the working tree; GitHub source archives will come from the tagged commit."
    }
}

New-CleanDirectory -Path $releaseRoot
if (-not $DryRun) {
    New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
}

foreach ($artifact in $artifacts) {
    $publishDir = Join-Path $stagingRoot $artifact.Folder
    $destinationPath = Join-Path $releaseRoot $artifact.File

    if (-not $DryRun) {
        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    }

    Invoke-CheckedCommand `
        -Executable "dotnet" `
        -Arguments @(
            "publish",
            $projectPath,
            "-c", "Release",
            "-r", $artifact.Runtime,
            "-o", $publishDir,
            "/p:Version=$versionNumber",
            "/p:AssemblyVersion=$assemblyVersion",
            "/p:FileVersion=$assemblyVersion",
            "/p:InformationalVersion=$versionTag"
        ) `
        -Description "Publish $($artifact.Runtime)"

    Write-Step "Package $($artifact.File)"
    Compress-ReleaseFolder -PublishDir $publishDir -DestinationPath $destinationPath -ArchiveKind $artifact.Archive
}

if ($IncludeSourceArchives) {
    New-SourceArchives
}

if ($WriteChecksums) {
    Write-HashManifest
}

if (-not $KeepStaging -and -not $DryRun -and (Test-Path -LiteralPath $stagingRoot)) {
    Assert-ChildPath -ParentPath $releaseRoot -ChildPath $stagingRoot
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

Write-Step "Release artifacts ready"
if (-not $DryRun) {
    Get-ChildItem -LiteralPath $releaseRoot -File | Sort-Object Name | Select-Object Name, Length, LastWriteTime
}
