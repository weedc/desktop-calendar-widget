$ErrorActionPreference = 'Stop'
$publicRoot = $PSScriptRoot
$publicDist = Join-Path $publicRoot 'dist'
$publicStage = Join-Path $publicDist 'DesktopCalendarWidget-v5.8.1'
New-Item -ItemType Directory -Path $publicStage -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publicRoot 'bin\GoogleCalendarWidget.exe') -Destination (Join-Path $publicStage 'GoogleCalendarWidget-v5.8.1.exe') -Force
foreach ($publicFile in @('calendar.ico', 'install.ps1', 'README.md', 'QA_REPORT.md', 'register_dot.py')) {
    Copy-Item -LiteralPath (Join-Path $publicRoot $publicFile) -Destination $publicStage -Force
}
# Refuse unexpected staging files instead of silently publishing stale data.
$publicAllowed = @('GoogleCalendarWidget-v5.8.1.exe', 'calendar.ico', 'install.ps1', 'README.md', 'QA_REPORT.md', 'register_dot.py')
$publicEntries = @(Get-ChildItem -LiteralPath $publicStage -Recurse -Force)
if ($publicEntries.Count -ne $publicAllowed.Count -or @($publicEntries | Where-Object { $_.PSIsContainer -or $_.Name -notin $publicAllowed -or $_.DirectoryName -ne $publicStage }).Count -gt 0) {
    throw '배포 폴더에 허용하지 않은 파일이 있습니다. 개인정보와 이전 파일을 확인한 뒤 다시 패키징하세요.'
}
$publicZip = Join-Path $publicDist 'DesktopCalendarWidget-v5.8.1-win-x64.zip'
Compress-Archive -LiteralPath $publicStage -DestinationPath $publicZip -Force
Get-FileHash -LiteralPath $publicZip -Algorithm SHA256 | ForEach-Object { $_.Hash.ToLowerInvariant() + '  ' + [IO.Path]::GetFileName($publicZip) } | Set-Content -LiteralPath (Join-Path $publicDist 'SHA256SUMS.txt') -Encoding ascii
Write-Output $publicZip
