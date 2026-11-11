$ErrorActionPreference = 'Stop'
$widgetInstall = Join-Path $env:LOCALAPPDATA 'Programs\GoogleCalendarWidget'
$widgetExeName = 'GoogleCalendarWidget-v5.2.exe'
$widgetSource = Join-Path $PSScriptRoot 'bin\GoogleCalendarWidget.exe'
if (-not (Test-Path -LiteralPath $widgetSource)) { $widgetSource = Join-Path $PSScriptRoot $widgetExeName }
if (-not (Test-Path -LiteralPath $widgetSource)) { throw '실행 파일을 찾을 수 없습니다. 소스 배포본에서는 build.ps1을 먼저 실행하세요.' }
New-Item -ItemType Directory -Path $widgetInstall -Force | Out-Null
$widgetTarget = Join-Path $widgetInstall $widgetExeName
# Reinstalling the same version also works while its MCP server is running.
if (-not (Test-Path -LiteralPath $widgetTarget) -or (Get-FileHash -LiteralPath $widgetSource).Hash -ne (Get-FileHash -LiteralPath $widgetTarget).Hash) {
    Copy-Item -LiteralPath $widgetSource -Destination $widgetTarget -Force
}
foreach ($widgetFile in @('calendar.ico', 'README.md', 'QA_REPORT.md')) {
    $widgetAsset = Join-Path $PSScriptRoot $widgetFile
    if (Test-Path -LiteralPath $widgetAsset) { Copy-Item -LiteralPath $widgetAsset -Destination $widgetInstall -Force }
}
$widgetDesktop = [Environment]::GetFolderPath('Desktop')
$widgetShell = New-Object -ComObject WScript.Shell
$widgetShortcutPath = Join-Path $widgetDesktop '팀 캘린더 위젯.lnk'
$widgetShortcut = $widgetShell.CreateShortcut($widgetShortcutPath)
$widgetShortcut.TargetPath = $widgetTarget
$widgetShortcut.WorkingDirectory = $widgetInstall
$widgetShortcut.IconLocation = (Join-Path $widgetInstall 'calendar.ico') + ',0'
$widgetShortcut.Description = '바탕화면 투명 캘린더 · 일정과 개인 메모'
$widgetShortcut.Save()
# Preserve the user's choice; only upgrade a login entry owned by this app.
$widgetRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$widgetOldStartup = (Get-ItemProperty -LiteralPath $widgetRun -Name 'DesktopCalendarWidget' -ErrorAction SilentlyContinue).DesktopCalendarWidget
if ($widgetOldStartup -and $widgetOldStartup.Contains($widgetInstall)) {
    Set-ItemProperty -LiteralPath $widgetRun -Name 'DesktopCalendarWidget' -Value ('"' + $widgetTarget + '"')
}
Write-Output "설치 완료: $widgetTarget"
Write-Output "실행 아이콘: $widgetShortcutPath"
