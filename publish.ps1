<#
.SYNOPSIS
    Publishes DesktopEnglish as a self-contained single-file Windows executable.

.DESCRIPTION
    Produces one .exe that runs on machines without .NET installed, and places it in the repository
    root so the shipped artifact sits next to everything else. WPF and EF Core are not trimmable, so
    trimming is disabled and the native SQLite libraries are bundled inside the single file.

    The executable is portable: it runs from wherever it is placed, writes its data to
    %LOCALAPPDATA%\DesktopEnglish and registers a per-user uninstall entry on first launch. Windows can
    therefore remove it from Apps & features, or the user can use the tray menu's "卸载程序" entry.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",

    # Staging folder used by the publish step. Kept out of the repository root and safe to delete.
    [string]$StageDir = "publish",

    # Where the finished executable is copied. Defaults to the repository root.
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

# Copy the executable to the requested destination (the repository root by default). The PDB is a
# build-time artifact and is not part of the shipped single-file application.
$outputDirPath = Join-Path $root $OutputDir
if (-not (Test-Path $outputDirPath)) {
    New-Item -ItemType Directory -Path $outputDirPath -Force | Out-Null
}

$targetExe = Join-Path $outputDirPath "DesktopEnglish.exe"
Copy-Item -LiteralPath $publishedExe -Destination $targetExe -Force

$size = [math]::Round((Get-Item $targetExe).Length / 1MB, 1)
Write-Host "Published $targetExe ($size MB)" -ForegroundColor Green
Write-Host "Staging output left in $stage (safe to delete)." -ForegroundColor DarkGray
