# A borderless full-screen window — what a browser in F11, a video player or a borderless game looks like to
# Windows: no frame, covering the whole display including the taskbar. With -TopMost it is itself topmost.
param([Parameter(Mandatory)] [string] $Title, [switch] $TopMost)
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object Windows.Forms.Form
$form.Text = $Title
$form.FormBorderStyle = 'None'
$form.StartPosition = 'Manual'
$form.Bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
$form.TopMost = [bool]$TopMost
$form.BackColor = [Drawing.Color]::FromArgb(32, 32, 48)
[void]$form.ShowDialog()
