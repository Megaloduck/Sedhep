param(
  [string]$Invoke = "",
  [string]$Out = "",
  [switch]$CropClient,
  [switch]$List
)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing

$process = Get-Process Sedhep.Desktop -ErrorAction Stop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::RootElement
$condition = New-Object System.Windows.Automation.PropertyCondition(
  [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
if ($null -eq $window) { throw "No Sedhep automation window" }

if ($List) {
  $all = $window.FindAll([System.Windows.Automation.TreeScope]::Descendants,
    [System.Windows.Automation.Condition]::TrueCondition)
  foreach ($element in $all) {
    $type = $element.Current.ControlType.ProgrammaticName
    $name = $element.Current.Name
    if ($name -ne "" -and ($type -like "*Button*" -or $type -like "*Edit*" -or $type -like "*Tab*")) {
      Write-Output "$type :: $name"
    }
  }
  return
}

if ($Invoke -ne "") {
  $nameCondition = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, $Invoke)
  $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCondition)
  if ($null -eq $button) { throw "No control named '$Invoke'" }
  $pattern = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
  $pattern.Invoke()
  Start-Sleep -Milliseconds 900
}

if ($Out -ne "") {
  Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1 | Out-Null
  $handle = $process.MainWindowHandle
  Add-Type @"
using System;
using System.Runtime.InteropServices;
public class Shot {
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, int flags);
  public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@
  $rect = New-Object Shot+RECT
  [Shot]::GetWindowRect($handle, [ref]$rect) | Out-Null
  $width = $rect.Right - $rect.Left
  $height = $rect.Bottom - $rect.Top
  $bitmap = New-Object System.Drawing.Bitmap $width, $height
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $hdc = $graphics.GetHdc()
  [Shot]::PrintWindow($handle, $hdc, 2) | Out-Null
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
  Write-Output "$Out"
}
