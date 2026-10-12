#Requires -Version 5.1
<#
    Standalone uninstaller for the "背单词" (DesktopEnglish) app.

    Removes the whole program from this machine:
      - stops any running copy,
      - deletes every DesktopEnglish.exe that makes up the program (the copy next to this
        script, the published copy under publish\, and any build output copy),
      - removes the publish output folder,
      - deletes the per-user uninstall entry and the auto-start value,
      - deletes the Toast notification registration (COM activator + AppUserModelId),
      - deletes the user data directory under %LOCALAPPDATA%.

    Run uninstall.cmd (double-click) or: powershell -ExecutionPolicy Bypass -File uninstall.ps1
    Pass /y to skip the confirmation prompt.
#>

$ErrorActionPreference = 'Continue'

function Test-Yes {
    foreach ($arg in $args) {
        if ("$arg" -match '^(?i)[-/]*y(es)?$') { return $true }
    }
    return $false
}

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$exe = Join-Path $here 'DesktopEnglish.exe'
$data = Join-Path $env:LOCALAPPDATA 'DesktopEnglish'
$publishDir = Join-Path $here 'publish'

Write-Host ''
Write-Host '  ============================================'
Write-Host '    卸载「背单词」(DesktopEnglish)'
Write-Host '  ============================================'
Write-Host "    程序目录 : $here"
Write-Host "    用户数据 : $data"
Write-Host ''

if (-not (Test-Yes @args)) {
    $answer = Read-Host '确定要删除整个程序吗?输入 Y 删除,其它输入取消'
    if ($answer -notmatch '^(?i)\s*y') {
        Write-Host '已取消,没有做任何改动。'
        exit 1
    }
}

Write-Host '[1/5] 停止正在运行的程序 ...'
Get-Process -Name 'DesktopEnglish' -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); [void]$_.WaitForExit(5000) } catch { }
}
Start-Sleep -Milliseconds 800

Write-Host '[2/5] 清理注册表(卸载项 / 开机启动 / 通知注册) ...'
$aumid = $exe -replace '\\', '/'
$hash = [System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($aumid))
$classId = [guid]::ParseExact((($hash[0..15] | ForEach-Object { $_.ToString('x2') }) -join ''), 'N')

$user = [Microsoft.Win32.Registry]::CurrentUser

try {
    $run = $user.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
    if ($run) { $run.DeleteValue('DesktopEnglish', $false); $run.Close() }
} catch { }

foreach ($keyPath in @(
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopEnglish',
        "Software\Classes\AppUserModelId\$aumid",
        "Software\Classes\CLSID\{$($classId.ToString('D'))}")) {
    try { $user.DeleteSubKeyTree($keyPath, $false) } catch { }
}

$toastDir = Join-Path $env:LOCALAPPDATA (Join-Path 'ToastNotificationManagerCompat\Apps' $classId.ToString('D').ToUpperInvariant())
if (Test-Path -LiteralPath $toastDir) {
    try { [System.IO.Directory]::Delete($toastDir, $true) } catch { }
}

Write-Host '[3/5] 删除用户数据 ...'
if (Test-Path -LiteralPath $data) {
    try { [System.IO.Directory]::Delete($data, $true) } catch { }
}

Write-Host '[4/5] 删除整个程序的 exe ...'
$targets = New-Object System.Collections.Generic.List[string]
$targets.Add($exe)
try {
    Get-ChildItem -LiteralPath $here -Recurse -Force -File -Filter 'DesktopEnglish.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notlike '*\.git\*' } |
        ForEach-Object { $targets.Add($_.FullName) }
} catch { }

foreach ($target in ($targets | Sort-Object -Unique)) {
    for ($i = 0; $i -lt 30; $i++) {
        if (-not (Test-Path -LiteralPath $target)) { break }
        try { [System.IO.File]::Delete($target) } catch { }
        if (-not (Test-Path -LiteralPath $target)) { break }
        Start-Sleep -Seconds 1
    }
    if (Test-Path -LiteralPath $target) {
        Write-Host "    [!] 未能删除: $target"
    } else {
        Write-Host "    [OK] 已删除: $target"
    }
}

if (Test-Path -LiteralPath $publishDir) {
    try {
        [System.IO.Directory]::Delete($publishDir, $true)
        Write-Host "    [OK] 已删除: $publishDir"
    } catch {
        Write-Host "    [!] 未能删除: $publishDir"
    }
}

Write-Host '[5/5] 完成。'
$leftover = @()
try {
    $leftover = Get-ChildItem -LiteralPath $here -Recurse -Force -File -Filter 'DesktopEnglish.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notlike '*\.git\*' } |
        Select-Object -ExpandProperty FullName
} catch { }
if ($leftover.Count -gt 0) {
    Write-Host '    仍有未删除的程序文件:'
    $leftover | ForEach-Object { Write-Host "      $_" }
} else {
    Write-Host '    程序的可执行文件已全部删除。'
}
if (Test-Path -LiteralPath $data) {
    Write-Host "    [!] 用户数据目录未能删除: $data"
}
Write-Host ''