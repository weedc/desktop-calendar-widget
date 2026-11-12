param([string]$WidgetBuildDirectory = '')
$ErrorActionPreference = 'Stop'
$sourceDir = $PSScriptRoot
$outputDir = if ([string]::IsNullOrWhiteSpace($WidgetBuildDirectory)) { Join-Path $sourceDir 'bin' } else { [IO.Path]::GetFullPath($WidgetBuildDirectory) }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $sourceDir 'calendar.ico'))) { & (Join-Path $sourceDir 'make-icon.ps1') }
$widgetSources = @('DesktopCalendar.cs', 'CalendarResize.cs', 'ResizeChecks.cs', 'DesktopBridge.cs', 'CalendarData.cs', 'CalendarFeatures.cs', 'CalendarDialogs.cs', 'SettingsDialog.cs', 'SimpleSettingsDialog.cs', 'AdvancedSettingsDialog.cs', 'SettingsDraft.cs', 'AccountDiscovery.cs', 'ChromeConnection.cs', 'CodexIntegration.cs', 'CodexDialog.cs', 'EmojiData.cs', 'CalendarPresentation.cs', 'EmojiDialog.cs', 'CalendarSourceList.cs', 'StartupRegistration.cs', 'DailySummary.cs', 'UpdatePolicy.cs', 'UpdateInstaller.cs', 'UpdateNotifications.cs', 'UpdateChecks.cs', 'SelfChecks.cs', 'ConnectionChecks.cs', 'SettingsChecks.cs', 'AssemblyInfo.cs') | ForEach-Object { Join-Path $sourceDir $_ }
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$outputDir\GoogleCalendarWidget.exe" "/win32manifest:$sourceDir\app.manifest" "/win32icon:$sourceDir\calendar.ico" "/resource:$sourceDir\GoogleCalendarDom.json,TeamCalendar.GoogleCalendarDom.json" "/resource:$sourceDir\UpdateInstall.ps1,TeamCalendar.UpdateInstall.ps1" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:System.Xml.dll /reference:System.Web.Extensions.dll "/reference:$framework\WPF\UIAutomationClient.dll" "/reference:$framework\WPF\UIAutomationTypes.dll" "/reference:$framework\WPF\WindowsBase.dll" $widgetSources
if ($LASTEXITCODE -ne 0) { throw '컴파일 실패' }
Write-Output (Join-Path $outputDir 'GoogleCalendarWidget.exe')
