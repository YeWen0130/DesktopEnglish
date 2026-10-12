<#
.SYNOPSIS
    Publishes DesktopEnglish as a self-contained executable and builds the standalone uninstaller.

.DESCRIPTION
    Produces one .exe that runs on machines without .NET installed, and places it in the repository
    root so the shipped artifact sits next to everything else. WPF and EF Core are not trimmable, so
    trimming is disabled and the native SQLite libraries are bundled inside the single file.

    The executable is portable: it runs from wherever it is placed, writes its data to
    %LOCALAPPDATA%\DesktopEnglish and registers a per-user uninstall entry on first launch.

    Also builds tools\DesktopEnglish.Uninstaller into Uninstall.exe and copies it to the same folder.
    That tool is the standalone uninstaller: it removes the registry entries, the user data and every
    file in the folder it is run from, then deletes itself, leaving the empty directory behind.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",

    # Staging folders used by the publish steps. Kept out of the repository root and safe to delete.
    [string]$StageDir = "publish",
    [string]$UninstallerStageDir = "publish-uninstall",

    # Where the finished executables are copied. Defaults to the repository root.
    [string]$OutputDir = "."
)

$ErrorActionPreference = "Stop"

$dotnet = if (Test-Path "C:\Users\YE\.dotnet\dotnet.exe") {
    "C:\Users\YE\.dotnet\dotnet.exe"
} else {
    "dotnet"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$stage = Join-Path $root $StageDir
if (Test-Path $stage) {
    Remove-Item $stage -Recurse -Force
}

Write-Host "Publishing DesktopEnglish ($Configuration, $Runtime, self-contained single file)" -ForegroundColor Cyan
& $dotnet publish "src\DesktopEnglish.App\DesktopEnglish.App.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $stage `
    --nologo `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$publishedExe = Join-Path $stage "DesktopEnglish.exe"
if (-not (Test-Path $publishedExe)) {
    throw "Expected output $publishedExe was not produced."
}

$uninstallerStage = Join-Path $root $UninstallerStageDir
if (Test-Path $uninstallerStage) {
    Remove-Item $uninstallerStage -Recurse -Force
}

Write-Host "Publishing the standalone uninstaller ($Runtime, self-contained single file)" -ForegroundColor Cyan
& $dotnet publish "tools\DesktopEnglish.Uninstaller\DesktopEnglish.Uninstaller.csproj" `
    -c Release `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o $uninstallerStage `
    --nologo `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "Uninstaller publish failed with exit code $LASTEXITCODE."
}

$publishedUninstaller = Join-Path $uninstallerStage "Uninstall.exe"
if (-not (Test-Path $publishedUninstaller)) {
    throw "Expected output $publishedUninstaller was not produced."
}

# Copy both executables to the requested destination (the repository root by default). The PDBs are
# build-time artifacts and are not part of the shipped single-file applications.
$outputDirPath = Join-Path $root $OutputDir
if (-not (Test-Path $outputDirPath)) {
    New-Item -ItemType Directory -Path $outputDirPath -Force | Out-Null
}

$targetExe = Join-Path $outputDirPath "DesktopEnglish.exe"
Copy-Item -LiteralPath $publishedExe -Destination $targetExe -Force

$targetUninstaller = Join-Path $outputDirPath "Uninstall.exe"
Copy-Item -LiteralPath $publishedUninstaller -Destination $targetUninstaller -Force

$appSize = [math]::Round((Get-Item $targetExe).Length / 1MB, 1)
$uninstallerSize = [math]::Round((Get-Item $targetUninstaller).Length / 1MB, 1)
Write-Host "Published $targetExe ($appSize MB)" -ForegroundColor Green
Write-Host "Published $targetUninstaller ($uninstallerSize MB)" -ForegroundColor Green
Write-Host "Staging output left in $stage and $uninstallerStage (safe to delete)." -ForegroundColor DarkGray