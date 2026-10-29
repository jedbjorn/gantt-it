<#
  inspect-msi.ps1 - print and check what a Gantt-It MSI installs (Windows only).

    powershell -File installer/inspect-msi.ps1 -Msi <path.msi> -Version 1.0.0-rc.1
    powershell -File installer/inspect-msi.ps1 -Msi <path.msi> -Version 0.0.0-dev -AllowBuildMetadata

  Reads the MSI database through the Windows Installer COM API and prints
  the package identity (ProductVersion, UpgradeCode, ProductCode, install
  scope), every file with its install path and the folders removed on
  uninstall. Then extracts the package with an administrative install
  (msiexec /a: unpacks files only, installs nothing) and reads the version
  resource of GanttIt.Revit.dll for each Revit year.

  -Version is the version the build was asked to stamp (the release tag
  without its v, or the dev version). Fails unless:
    - UpgradeCode, name and manufacturer are Gantt-It's;
    - the package is per-user and needs no elevation;
    - the MSI is named GanttIt-<Version>.msi and its ProductVersion is the
      numeric a.b.c of -Version;
    - it holds exactly the expected add-in files for both Revit years;
    - it removes GanttIt\, Resources\ and icons\ (per year) on uninstall,
      and never Addins\<year>\;
    - each year's GanttIt.Revit.dll has ProductVersion (the assembly's
      InformationalVersion, which the add-in stamps as AppVersion) equal to
      -Version; with -AllowBuildMetadata a "+<commit>" suffix is accepted
      (dev builds).
  CI runs it on every MSI it builds.
#>
param(
  [Parameter(Mandatory = $true)] [string] $Msi,
  [Parameter(Mandatory = $true)] [string] $Version,
  [switch] $AllowBuildMetadata
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
$numeric = [regex]::Match($Version, '^\d+\.\d+\.\d+').Value
if (-not $numeric) { $problems += "-Version '$Version' does not start with a numeric a.b.c" }
if ($props['ProductVersion'] -ne $numeric) {
  $problems += "ProductVersion is $($props['ProductVersion']), expected $numeric (from $Version)"
}
$msiName = Split-Path -Leaf $Msi
if ($msiName -ne "GanttIt-$Version.msi") { $problems += "MSI is named $msiName, expected GanttIt-$Version.msi" }
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

# Folder cleanup on uninstall (RemoveFile rows with no file name = RemoveFolder).
$removed = @(
  foreach ($r in (Query 'SELECT `DirProperty`, `FileName`, `InstallMode` FROM `RemoveFile`' 3)) {
    if ([string]::IsNullOrEmpty($r[1]) -and ([int]$r[2] -band 2)) { DirPath $r[0] }
  }
) | Sort-Object
Write-Host "Folders removed on uninstall ($($removed.Count)):"
$removed | ForEach-Object { Write-Host "  $_" }
foreach ($year in '2026', '2027') {
  $base = "AppDataFolder\Autodesk\Revit\Addins\$year"
  foreach ($sub in 'GanttIt', 'GanttIt\Resources', 'GanttIt\Resources\icons') {
    if ($removed -notcontains "$base\$sub") { $problems += "uninstall does not remove $base\$sub" }
  }
  if ($removed -contains $base) { $problems += "uninstall removes $base (Revit's folder, shared with other add-ins)" }
}

# Version resource of the packed add-in: extract with an administrative
# install (unpacks the files to a folder; installs and registers nothing).
$msiFull = (Resolve-Path $Msi).Path
$extract = Join-Path ([System.IO.Path]::GetTempPath()) ("ganttit-msi-" + [guid]::NewGuid().ToString('N'))
$log = "$extract.log"
$p = Start-Process -FilePath 'msiexec.exe' -Wait -PassThru -ArgumentList @(
  '/a', "`"$msiFull`"", '/qn', "TARGETDIR=`"$extract`"", '/l*v', "`"$log`"")
if ($p.ExitCode -ne 0) {
  $problems += "administrative extract (msiexec /a) failed with exit code $($p.ExitCode); log: $log"
} else {
  foreach ($year in '2026', '2027') {
    $dll = Get-ChildItem -Path $extract -Recurse -Filter 'GanttIt.Revit.dll' |
      Where-Object { $_.FullName -match "\\Addins\\$year\\GanttIt\\GanttIt\.Revit\.dll$" } |
      Select-Object -First 1
    if ($null -eq $dll) { $problems += "extract has no Addins\$year\GanttIt\GanttIt.Revit.dll"; continue }
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dll.FullName)
    Write-Host ("GanttIt.Revit.dll {0}: ProductVersion {1}  FileVersion {2}" -f $year, $vi.ProductVersion, $vi.FileVersion)
    $ok = ($vi.ProductVersion -eq $Version) -or
          ($AllowBuildMetadata -and $vi.ProductVersion.StartsWith("$Version+"))
    if (-not $ok) { $problems += "Addins\$year GanttIt.Revit.dll ProductVersion is '$($vi.ProductVersion)', expected '$Version'" }
  }
  Remove-Item -Recurse -Force $extract -ErrorAction SilentlyContinue
}

if ($problems.Count) {
  $problems | ForEach-Object { Write-Host "::error::$_" }
  exit 1
}
Write-Host 'MSI identity, layout, folder cleanup and version stamps OK.'
