<#
  inspect-msi.ps1 — print and check what a Gantt-It MSI installs (Windows only).

    powershell -File installer/inspect-msi.ps1 -Msi GanttIt-1.0.0.msi [-ExpectVersion 1.0.0]

  Reads the MSI database through the Windows Installer COM API (no install,
  no admin) and prints the package identity (ProductVersion, UpgradeCode,
  ProductCode, install scope) and every file with its install path. Fails
  when the identity or the layout is not what Product.wxs promises: the
  fixed UpgradeCode, a per-user package that needs no elevation, the
  expected ProductVersion, and exactly the expected add-in files in both
  Revit years' folders (no more, no fewer).
  CI runs it on every MSI it builds.
#>
param(
  [Parameter(Mandatory = $true)] [string] $Msi,
  [string] $ExpectVersion = ''
)

$ErrorActionPreference = 'Stop'
$UpgradeCode = '{644FCAD5-E631-4441-9A11-75F175565136}'

function Invoke-Com($obj, [string] $name, [System.Reflection.BindingFlags] $kind, [object[]] $argList = @()) {
  return $obj.GetType().InvokeMember($name, $kind, $null, $obj, $argList)
}
$Call = [System.Reflection.BindingFlags]::InvokeMethod
$Get = [System.Reflection.BindingFlags]::GetProperty

$installer = New-Object -ComObject WindowsInstaller.Installer
$db = Invoke-Com $installer 'OpenDatabase' $Call @((Resolve-Path $Msi).Path, 0)

function Query([string] $sql, [int] $columns) {
  $view = Invoke-Com $db 'OpenView' $Call @($sql)
  Invoke-Com $view 'Execute' $Call | Out-Null
  $rows = @()
  while ($true) {
    $rec = Invoke-Com $view 'Fetch' $Call
    if ($null -eq $rec) { break }
    $row = @()
    for ($c = 1; $c -le $columns; $c++) { $row += , (Invoke-Com $rec 'StringData' $Get @($c)) }
    $rows += , $row
  }
  Invoke-Com $view 'Close' $Call | Out-Null
  return , $rows
}

$props = @{}
foreach ($r in (Query 'SELECT `Property`, `Value` FROM `Property`' 2)) { $props[$r[0]] = $r[1] }

$summary = Invoke-Com $db 'SummaryInformation' $Get @(0)
$wordCount = [int](Invoke-Com $summary 'Property' $Get @(15))
$template = Invoke-Com $summary 'Property' $Get @(7)

# Directory table -> full target paths (DefaultDir is "[short|]long[:source]").
$dirs = @{}
foreach ($r in (Query 'SELECT `Directory`, `Directory_Parent`, `DefaultDir` FROM `Directory`' 3)) {
  $name = ($r[2] -split ':')[0]
  $name = ($name -split '\|')[-1]
  $dirs[$r[0]] = @{ Parent = $r[1]; Name = $name }
}
function DirPath([string] $id) {
  $d = $dirs[$id]
  if ($null -eq $d) { return $id }
  # TARGETDIR is the root; its children are standard folders (AppDataFolder,
  # resolved by Windows Installer at install time), so print those by id.
  if ([string]::IsNullOrEmpty($d.Parent) -or $d.Parent -eq $id -or $d.Parent -eq 'TARGETDIR') { return $id }
  $parent = DirPath $d.Parent
  if ($d.Name -eq '.') { return $parent }
  return "$parent\$($d.Name)"
}
$compDir = @{}
foreach ($r in (Query 'SELECT `Component`, `Directory_` FROM `Component`' 2)) { $compDir[$r[0]] = $r[1] }
$files = @()
foreach ($r in (Query 'SELECT `FileName`, `Component_`, `FileSize` FROM `File`' 3)) {
  $name = ($r[0] -split '\|')[-1]
  $files += [pscustomobject]@{ Path = "$(DirPath $compDir[$r[1]])\$name"; Size = [int64]$r[2] }
}
$files = $files | Sort-Object Path

Write-Host "MSI:              $Msi"
Write-Host "ProductName:      $($props['ProductName'])"
Write-Host "Manufacturer:     $($props['Manufacturer'])"
Write-Host "ProductVersion:   $($props['ProductVersion'])"
Write-Host "UpgradeCode:      $($props['UpgradeCode'])"
Write-Host "ProductCode:      $($props['ProductCode'])"
Write-Host "ALLUSERS:         '$($props['ALLUSERS'])'  MSIINSTALLPERUSER: '$($props['MSIINSTALLPERUSER'])'"
Write-Host "Template:         $template   Word Count: $wordCount (8 = no elevation required)"
Write-Host "ARPURLUPDATEINFO: $($props['ARPURLUPDATEINFO'])"
Write-Host "Files ($($files.Count)):"
$files | ForEach-Object { Write-Host ("  {0,9}  {1}" -f $_.Size, $_.Path) }

$problems = @()
if ($props['UpgradeCode'] -ne $UpgradeCode) { $problems += "UpgradeCode is $($props['UpgradeCode']), expected $UpgradeCode" }
if ($props['ProductName'] -ne 'Gantt-It') { $problems += "ProductName is '$($props['ProductName'])'" }
if ($props['Manufacturer'] -ne 'Designs/OS') { $problems += "Manufacturer is '$($props['Manufacturer'])'" }
if ($props['ALLUSERS'] -and $props['ALLUSERS'] -ne '2') { $problems += "ALLUSERS is '$($props['ALLUSERS'])' (per-machine)" }
if (($wordCount -band 8) -eq 0) { $problems += "summary Word Count ${wordCount}: package requests elevation" }
if ($ExpectVersion -and $props['ProductVersion'] -ne $ExpectVersion) {
  $problems += "ProductVersion is $($props['ProductVersion']), expected $ExpectVersion"
}
# The exact file set, per Revit year (8 files each, 16 in all): PDBs ship,
# XML doc files do not (installer/stage.sh drops them). A file added to the
# add-in output must be added here too, deliberately.
$perYear = @(
  'GanttIt.addin',
  'GanttIt\GanttIt.Core.dll',
  'GanttIt\GanttIt.Core.pdb',
  'GanttIt\GanttIt.Revit.deps.json',
  'GanttIt\GanttIt.Revit.dll',
  'GanttIt\GanttIt.Revit.pdb',
  'GanttIt\Resources\icons\gantt_16.png',
  'GanttIt\Resources\icons\gantt_32.png'
)
$expected = foreach ($year in '2026', '2027') {
  foreach ($rel in $perYear) { "AppDataFolder\Autodesk\Revit\Addins\$year\$rel" }
}
$actual = @($files | ForEach-Object { $_.Path })
foreach ($want in $expected) {
  if ($actual -notcontains $want) { $problems += "missing $want" }
}
foreach ($have in $actual) {
  if ($expected -notcontains $have) { $problems += "unexpected $have" }
}
if ($actual.Count -ne $expected.Count) {
  $problems += "MSI has $($actual.Count) files, expected $($expected.Count)"
}

if ($problems.Count) {
  $problems | ForEach-Object { Write-Host "::error::$_" }
  exit 1
}
Write-Host 'MSI identity and layout OK.'
