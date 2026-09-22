<#
.SYNOPSIS
    Build script for DesktopEnglish.

.DESCRIPTION
    Resolves the local .NET 8 SDK (installed under the user profile so no admin rights are needed)
    and builds the whole solution. The global PATH is deliberately left untouched so the machine's
    own .NET runtimes keep working as before.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Debug"
)

$ErrorActionPreference = "Stop"

# The SDK lives in the user profile; fall back to whatever is on PATH for CI machines.
$dotnet = if (Test-Path "C:\Users\YE\.dotnet\dotnet.exe") {
    "C:\Users\YE\.dotnet\dotnet.exe"
} else {
    "dotnet"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "Building DesktopEnglish ($Configuration) with $dotnet" -ForegroundColor Cyan
& $dotnet build "DesktopEnglish.sln" -c $Configuration -v m --nologo
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

& $dotnet test "tests\DesktopEnglish.Tests\DesktopEnglish.Tests.csproj" -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) {
    throw "Tests failed with exit code $LASTEXITCODE."
}

Write-Host "Build and tests succeeded." -ForegroundColor Green
