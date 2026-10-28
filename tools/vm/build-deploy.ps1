<#
  build-deploy.ps1 — guest side (W10C-Testing). Expand a pushed source zip, build it for one or
  more Revit years, and (with -Deploy) install the output as a dev add-in.

  Usage (from the host):
    ./sc vm exec -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\GanttIt\tools\build-deploy.ps1 -Zip C:\GanttIt\stage\spike.zip -Name GanttIt.Spike -Project GanttIt.Spike\GanttIt.Spike.csproj -Years 2026,2027 -Deploy

  Layout produced:
    C:\GanttIt\src\<Name>\                                 expanded source
    <src>\<project dir>\bin\Debug R26\ , bin\Debug R27\       build output (OutputPath bin\$(Configuration)\)
    %AppData%\Autodesk\Revit\Addins\<year>\<Name>.addin       manifest (Assembly path: <Name>\<Name>.dll)
    %AppData%\Autodesk\Revit\Addins\<year>\<Name>\           DLLs
    C:\GanttIt\logs\build-<Name>-<year>.log                    build log
  Revit locks loaded DLLs: stop Revit (revit.ps1 -Action stop) before re-deploying.
  Arguments avoid spaces on purpose: `sc vm exec` goes through cmd.exe, so -Years maps to the
  "Debug R26"/"Debug R27" configurations here instead of passing quoted names.
#>
param(
  [Parameter(Mandatory)][string]$Zip,
  [Parameter(Mandatory)][string]$Name,
  [Parameter(Mandatory)][string]$Project,
  [string]$Years = '2026',   # comma-separated: -File passes arrays as one string
  [string]$Flavor = 'Debug',
  [switch]$Deploy
)
$ErrorActionPreference = 'Stop'
$src = "C:\GanttIt\src\$Name"
$logs = 'C:\GanttIt\logs'
New-Item -ItemType Directory -Force $logs | Out-Null
if (Test-Path $src) { Remove-Item $src -Recurse -Force }
Expand-Archive -Path $Zip -DestinationPath $src -Force
$proj = Join-Path $src $Project
$projDir = Split-Path $proj -Parent
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

foreach ($year in ($Years -split ',' | ForEach-Object { [int]$_.Trim() })) {
  $tag = if ($year -eq 2026) { 'R26' } elseif ($year -eq 2027) { 'R27' } else { throw "unsupported year $year" }
  $cfg = "$Flavor $tag"
  $log = Join-Path $logs "build-$Name-$year.log"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & dotnet build $proj -c $cfg -nologo -v:minimal *> $log
  $code = $LASTEXITCODE
  Write-Output ("build {0} [{1}] exit={2} in {3:n1}s (log {4})" -f $Name, $cfg, $code, $sw.Elapsed.TotalSeconds, $log)
  Get-Content $log | Select-String -Pattern 'error|warn' | Select-Object -First 30 | ForEach-Object { Write-Output ("  " + $_.Line) }
  if ($code -ne 0) { exit $code }

  if ($Deploy) {
    $out = Join-Path $projDir "bin\$cfg"
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $dest = Join-Path $addins $Name
    New-Item -ItemType Directory -Force $dest | Out-Null
    Get-ChildItem $dest -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item (Join-Path $out '*') $dest -Recurse -Force
    Move-Item (Join-Path $dest "$Name.addin") (Join-Path $addins "$Name.addin") -Force
    $dll = Get-Item (Join-Path $dest "$Name.dll")
    Write-Output ("deployed {0} -> {1} ({2:u}, {3} bytes)" -f $cfg, $dest, $dll.LastWriteTime, $dll.Length)
  }
}
