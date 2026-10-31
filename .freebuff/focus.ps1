Add-Type @"
using System;
using System.Runtime.InteropServices;
public class F { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c); }
"@
$p = Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[F]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
[F]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 700
