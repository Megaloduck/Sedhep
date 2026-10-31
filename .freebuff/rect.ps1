Add-Type @"
using System;
using System.Runtime.InteropServices;
public class R2 { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int Left, Top, Right, Bottom; } }
"@
$p = Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$r = New-Object R2+RECT
[R2]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
Write-Output ("{0},{1} {2}x{3}" -f $r.Left, $r.Top, ($r.Right-$r.Left), ($r.Bottom-$r.Top))
