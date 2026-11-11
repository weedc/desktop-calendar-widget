$ErrorActionPreference = 'Stop'
$publicRoot = $PSScriptRoot
$publicDist = Join-Path $publicRoot 'dist'
$publicStage = Join-Path $publicDist 'DesktopCalendarWidget-v5.4.1'
New-Item -ItemType Directory -Path $publicStage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publicRoot 'bin\GoogleCalendarWidget.exe') -Destination (Join-Path $publicStage 'GoogleCalendarWidget-v5.4.1.exe') -Force
foreach ($publicFile in @('calendar.ico', 'install.ps1', 'README.md', 'QA_REPORT.md', 'register_dot.py')) {
    Copy-Item -LiteralPath (Join-Path $publicRoot $publicFile) -Destination $publicStage -Force
}
$publicZip = Join-Path $publicDist 'DesktopCalendarWidget-v5.4.1-win-x64.zip'
Compress-Archive -LiteralPath $publicStage -DestinationPath $publicZip -Force
Get-FileHash -LiteralPath $publicZip -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($publicZip) } | Set-Content -LiteralPath (Join-Path $publicDist 'SHA256SUMS.txt') -Encoding ascii
Write-Output $publicZip
