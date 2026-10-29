param([string]$Out = "shot.png")

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NativeWin {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$p = Get-Process Sedhep.Desktop -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw "No Sedhep window found" }

$handle = $p.MainWindowHandle
[NativeWin]::ShowWindow($handle, 9) | Out-Null
[NativeWin]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 600

$rect = New-Object NativeWin+RECT
[NativeWin]::GetWindowRect($handle, [ref]$rect) | Out-Null
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
[NativeWin]::PrintWindow($handle, $hdc, 2) | Out-Null
$graphics.ReleaseHdc($hdc)
$graphics.Dispose()

$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Output "$Out ${width}x${height}"
