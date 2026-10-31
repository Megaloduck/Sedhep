param([string]$Text, [string]$Out = "")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$p = Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$typeCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edit = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $typeCond)
if ($null -eq $edit) { throw "No text field" }
$edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($Text)
Start-Sleep -Milliseconds 900
if ($Out -ne "") {
  $r = $window.Current.BoundingRectangle
  $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen((New-Object System.Drawing.Point ([int]$r.X), ([int]$r.Y)), [System.Drawing.Point]::Empty, (New-Object System.Drawing.Size ([int]$r.Width), ([int]$r.Height)))
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
  Write-Output $Out
}
