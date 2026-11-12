param([switch]$EnableAutoStart, [switch]$DisableAutoStart)
$ErrorActionPreference = 'Stop'
if ($EnableAutoStart -and $DisableAutoStart) { throw '자동 실행 켜기와 끄기는 동시에 지정할 수 없습니다.' }
$widgetInstall = Join-Path $env:LOCALAPPDATA 'Programs\GoogleCalendarWidget'
$widgetPreviouslyInstalled = @(Get-ChildItem -LiteralPath $widgetInstall -Filter 'GoogleCalendarWidget*.exe' -File -ErrorAction SilentlyContinue).Count -gt 0
$widgetExeName = 'GoogleCalendarWidget-v5.8.1.exe'
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
# Fresh installs enable auto-start; upgrades retain a saved choice.
$widgetRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$widgetPreferences = 'HKCU:\Software\DesktopCalendarWidget'
if (-not (Test-Path -LiteralPath $widgetRun)) { New-Item -Path $widgetRun -Force | Out-Null }
$widgetOwnedStartup = @()
$widgetRunValues = Get-ItemProperty -LiteralPath $widgetRun
foreach ($widgetProperty in $widgetRunValues.PSObject.Properties) {
    $widgetValue = [string]$widgetProperty.Value
    if ($widgetValue -match '^\s*"?([^"\r\n]+?GoogleCalendarWidget(?:-v[0-9.]+)?\.exe)"?(?:\s.*)?$') {
        $widgetOldExe = $Matches[1]
        if ([IO.Path]::GetDirectoryName($widgetOldExe) -eq $widgetInstall) { $widgetOwnedStartup += $widgetProperty.Name }
    }
}
$widgetSavedChoice = Get-ItemProperty -LiteralPath $widgetPreferences -Name AutoStart -ErrorAction SilentlyContinue
$widgetAutoStart = if ($EnableAutoStart) { $true } elseif ($DisableAutoStart) { $false } elseif ($null -ne $widgetSavedChoice) { [bool]$widgetSavedChoice.AutoStart } else { -not $widgetPreviouslyInstalled -or $widgetOwnedStartup.Count -gt 0 }
foreach ($widgetName in $widgetOwnedStartup) { Remove-ItemProperty -LiteralPath $widgetRun -Name $widgetName }
if ($widgetAutoStart) {
    New-ItemProperty -LiteralPath $widgetRun -Name 'DesktopCalendarWidget' -Value ('"' + $widgetTarget + '"') -PropertyType String -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $widgetPreferences)) { New-Item -Path $widgetPreferences -Force | Out-Null }
New-ItemProperty -LiteralPath $widgetPreferences -Name AutoStart -Value ([int]$widgetAutoStart) -PropertyType DWord -Force | Out-Null
Write-Output "설치 완료: $widgetTarget"
Write-Output "실행 아이콘: $widgetShortcutPath"

Write-Output ("로그인 시 자동 실행: " + $widgetAutoStart)
