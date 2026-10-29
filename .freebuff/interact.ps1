param(
  [string]$Action = "none",
  [int]$X = 0,
  [int]$Y = 0,
  [int]$Wheel = 0,
  [int]$Width = 0,
  [int]$Height = 0,
  [switch]$CropClient,
  [string]$Out = ""
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class NativeWin {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, int data, IntPtr extra);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$p = Get-Process Sedhep.Desktop -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { throw "No Sedhep window found" }

$handle = $p.MainWindowHandle
[NativeWin]::SetForegroundWindow($handle) | Out-Null
Start-Sleep -Milliseconds 500

if ($Width -gt 0 -and $Height -gt 0) {
  [NativeWin]::MoveWindow($handle, 60, 40, $Width, $Height, $true) | Out-Null
  Start-Sleep -Milliseconds 900
}

$rect = New-Object NativeWin+RECT
[NativeWin]::GetWindowRect($handle, [ref]$rect) | Out-Null

if ($Action -ne "none") {
  # The first click on an unfocused window only activates it, so activate on the title bar first.
  [NativeWin]::SetCursorPos($rect.Left + 200, $rect.Top + 14) | Out-Null
  Start-Sleep -Milliseconds 200
  [NativeWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
  [NativeWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  Start-Sleep -Milliseconds 300

  $screenX = $rect.Left + $X
  $screenY = $rect.Top + $Y
  [NativeWin]::SetCursorPos($screenX, $screenY) | Out-Null
  Start-Sleep -Milliseconds 250

  if ($Action -eq "click") {
    [NativeWin]::mouse_event(0x0002, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 80
    [NativeWin]::mouse_event(0x0004, 0, 0, 0, [IntPtr]::Zero)
  } elseif ($Action -eq "wheel") {
    [NativeWin]::mouse_event(0x0800, 0, 0, $Wheel, [IntPtr]::Zero)
  }

  Start-Sleep -Milliseconds 700
}

if ($Out -ne "") {
  $width = $rect.Right - $rect.Left
  $height = $rect.Bottom - $rect.Top
  $bitmap = New-Object System.Drawing.Bitmap $width, $height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $hdc = $graphics.GetHdc()
  [NativeWin]::PrintWindow($handle, $hdc, 2) | Out-Null
  $graphics.ReleaseHdc($hdc)
  $graphics.Dispose()

  if ($CropClient) {
    $client = New-Object System.Drawing.Rectangle 4, 32, ($width - 8), ($height - 39)
    $cropped = $bitmap.Clone($client, $bitmap.PixelFormat)
    $bitmap.Dispose()
    $bitmap = $cropped
  }

  $bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $bitmap.Dispose()
  Write-Output "$Out ${width}x${height}"
}
