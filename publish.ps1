<#
.SYNOPSIS
    Publishes DesktopEnglish as a self-contained single-file Windows executable.

.DESCRIPTION
    Produces one .exe (plus the extraction marker directory created on first run) that runs on
    machines without .NET installed. WPF and EF Core are not trimmable, so trimming is disabled and
    the native SQLite libraries are bundled inside the single file.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "publish"
)

$ErrorActionPreference = "Stop"

$dotnet = if (Test-Path "C:\Users\YE\.dotnet\dotnet.exe") {
    "C:\Users\YE\.dotnet\dotnet.exe"
} else {
    "dotnet"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$out = Join-Path $root $OutputDir
if (Test-Path $out) {
    Remove-Item $out -Recurse -Force
}

Write-Host "Publishing DesktopEnglish ($Configuration, $Runtime, self-contained single file)" -ForegroundColor Cyan
& $dotnet publish "src\DesktopEnglish.App\DesktopEnglish.App.csproj" `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $out `
    --nologo `
    -v m

if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE."
}

$exe = Join-Path $out "DesktopEnglish.exe"
if (-not (Test-Path $exe)) {
    throw "Expected output $exe was not produced."
}

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Published $exe ($size MB)" -ForegroundColor Green
