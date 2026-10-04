# The "other application" for foreground tests: a window with one text box. Whatever is typed into it is
# written to -Status, so the harness can prove its keystrokes reached this window and nothing else.
param([Parameter(Mandatory)] [string] $Title, [Parameter(Mandatory)] [string] $Status)
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object Windows.Forms.Form
$form.Text = $Title
$form.StartPosition = 'CenterScreen'
$form.Width = 560
$form.Height = 360
$box = New-Object Windows.Forms.TextBox
$box.Multiline = $true
$box.Dock = 'Fill'
$box.Add_TextChanged({ Set-Content -LiteralPath $Status -Value $box.Text })
$form.Controls.Add($box)
[void]$form.ShowDialog()
