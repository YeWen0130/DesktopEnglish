#Requires -Version 5.1
<#
    Standalone uninstaller for the portable "背单词" (DesktopEnglish) app.

    Removes the whole application from this machine:
      - stops any running copy,
      - deletes the per-user uninstall entry and the auto-start value,
      - deletes the Toast notification registration (COM activator + AppUserModelId),
      - deletes the user data directory under %LOCALAPPDATA%,
      - deletes DesktopEnglish.exe that sits next to this script.

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

Write-Host ''
Write-Host '  ============================================'
Write-Host '    卸载「背单词」(DesktopEnglish)'
Write-Host '  ============================================'
Write-Host "    程序文件 : $exe"
Write-Host "    用户数据 : $data"
Write-Host ''

if (-not (Test-Yes @args)) {
    $answer = Read-Host '确定要删除整个软件吗?输入 Y 删除,其它输入取消'
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

Write-Host '[4/5] 删除程序文件 ...'
for ($i = 0; $i -lt 30; $i++) {
    if (-not (Test-Path -LiteralPath $exe)) { break }
    try { [System.IO.File]::Delete($exe) } catch { }
    if (-not (Test-Path -LiteralPath $exe)) { break }
    Start-Sleep -Seconds 1
}

Write-Host '[5/5] 完成:'
if (Test-Path -LiteralPath $exe) {
    Write-Host "    [!] 程序文件未能删除,请手动删除: $exe"
} else {
    Write-Host "    [OK] 已删除: $exe"
}
if (Test-Path -LiteralPath $data) {
    Write-Host "    [!] 用户数据目录未能删除: $data"
}
Write-Host ''