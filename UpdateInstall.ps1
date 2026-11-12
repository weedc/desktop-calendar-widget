param([Parameter(Mandatory=$true)][string]$PlanPath)
$ErrorActionPreference = 'Stop'
$updatePlan = Get-Content -LiteralPath $PlanPath -Raw -Encoding UTF8 | ConvertFrom-Json
$previousExe = [IO.Path]::GetFullPath([string]$updatePlan.installed)
$updatedExe = [IO.Path]::GetFullPath([string]$updatePlan.target)
$installDirectory = [IO.Path]::GetDirectoryName($previousExe)
if ([IO.Path]::GetDirectoryName($updatedExe) -ne $installDirectory -or
    [IO.Path]::GetFileName($previousExe) -notmatch '^GoogleCalendarWidget(?:-v[0-9.]+)?\.exe$' -or
    [IO.Path]::GetFileName($updatedExe) -ne ('GoogleCalendarWidget-v' + $updatePlan.version + '.exe') -or
    -not (Test-Path -LiteralPath $updatedExe)) { throw 'Invalid update destination' }
$oldProcess = Get-Process -Id ([int]$updatePlan.parent) -ErrorAction SilentlyContinue
if ($oldProcess -and -not $oldProcess.WaitForExit(30000)) { exit 1 }
$savedStartup = @{}
$shortcutBackup = $null
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
function Start-WidgetExecutable([string]$Executable) {
    $launch = New-Object System.Diagnostics.ProcessStartInfo
    $launch.FileName = $Executable
    $launch.WorkingDirectory = $installDirectory
    $launch.UseShellExecute = $false
    [System.Diagnostics.Process]::Start($launch) | Out-Null
}
try {
    $runValues = Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue
    foreach ($property in $runValues.PSObject.Properties) {
        if (([string]$property.Value).Trim('"') -eq $previousExe) {
            $savedStartup[$property.Name] = [string]$property.Value
            Set-ItemProperty -LiteralPath $runKey -Name $property.Name -Value ('"' + $updatedExe + '"')
        }
    }
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) '팀 캘린더 위젯.lnk'
    if (Test-Path -LiteralPath $shortcutPath) {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -eq $previousExe) {
            $shortcutBackup = Join-Path ([IO.Path]::GetDirectoryName($PlanPath)) 'previous.lnk'
            Copy-Item -LiteralPath $shortcutPath -Destination $shortcutBackup
            $shortcut.TargetPath = $updatedExe
            $shortcut.WorkingDirectory = $installDirectory
            $shortcut.Save()
        }
    }
    Start-WidgetExecutable $updatedExe
} catch {
    foreach ($entry in $savedStartup.GetEnumerator()) { try { Set-ItemProperty -LiteralPath $runKey -Name $entry.Key -Value $entry.Value } catch {} }
    if ($shortcutBackup) { try { Copy-Item -LiteralPath $shortcutBackup -Destination $shortcutPath -Force } catch {} }
    Start-WidgetExecutable $previousExe
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.MessageBox]::Show('업데이트를 마치지 못해 이전 버전으로 돌아왔습니다. 다시 시도해 주세요.', '캘린더 업데이트') | Out-Null
    exit 1
}
