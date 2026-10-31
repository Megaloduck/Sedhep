param([string]$Name, [string]$Out = "")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
$process = Get-Process Sedhep.Desktop | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
$window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name)
$el = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
if ($null -eq $el) { throw "No control named '$Name'" }
$r = $el.Current.BoundingRectangle
Write-Output ("{0} :: {1},{2} {3}x{4}" -f $Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
if ($Out -ne "") {
  $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
  Add-Type -AssemblyName System.Windows.Forms
  $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
  $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}
