$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class CalendarIconNative { [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr icon); }'
$widgetBitmap = [System.Drawing.Bitmap]::new(64, 64)
$widgetGraphics = [System.Drawing.Graphics]::FromImage($widgetBitmap)
$widgetGraphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$widgetGraphics.Clear([System.Drawing.Color]::Transparent)
$widgetBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(30, 66, 82))
$widgetGraphics.FillRectangle($widgetBrush, 5, 7, 54, 52)
$widgetBrush.Dispose()
$widgetBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(105, 186, 202))
$widgetGraphics.FillRectangle($widgetBrush, 5, 7, 54, 12)
$widgetBrush.Dispose()
$widgetFont = [System.Drawing.Font]::new('Segoe UI', 25, [System.Drawing.FontStyle]::Bold)
$widgetGraphics.DrawString('31', $widgetFont, [System.Drawing.Brushes]::White, 7, 17)
$widgetNativeHandle = $widgetBitmap.GetHicon()
$widgetIcon = [System.Drawing.Icon]::FromHandle($widgetNativeHandle)
$widgetStream = [System.IO.File]::Create((Join-Path $PSScriptRoot 'calendar.ico'))
$widgetIcon.Save($widgetStream)
$widgetStream.Dispose()
$widgetIcon.Dispose()
[CalendarIconNative]::DestroyIcon($widgetNativeHandle) | Out-Null
$widgetFont.Dispose()
$widgetGraphics.Dispose()
$widgetBitmap.Dispose()
