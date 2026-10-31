Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public class W {
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
  public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  public struct RECT { public int Left, Top, Right, Bottom; }
}
"@
$pid2 = (Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1).Id
$cb = [W+EnumWindowsProc]{
  param($h, $l)
  $p = 0
  [W]::GetWindowThreadProcessId($h, [ref]$p) | Out-Null
  if ($p -eq $pid2 -and [W]::IsWindowVisible($h)) {
    $sb = New-Object System.Text.StringBuilder 256
    [W]::GetWindowText($h, $sb, 256) | Out-Null
    $r = New-Object W+RECT
    [W]::GetWindowRect($h, [ref]$r) | Out-Null
    Write-Output ("{0} [{1}] {2}x{3} at {4},{5} :: {6}" -f $h, $p, ($r.Right-$r.Left), ($r.Bottom-$r.Top), $r.Left, $r.Top, $sb.ToString())
  }
  return $true
}
[W]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null
