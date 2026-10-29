<#
  build-deploy.ps1 — guest side (W10C-Testing). Build Gantt-It from a pushed source zip and
  (with -Deploy) install it as a dev add-in. Use it when the CI artifact is not available;
  otherwise deploy-artifact.ps1 is quicker.

  Host:
    python3 tools/vm/pack.py . .sc-state/local/vm-stage/gantt-it-src.zip
    ./sc vm push <abs>/.sc-state/local/vm-stage/gantt-it-src.zip C:\GanttIt\stage\gantt-it-src.zip
  Guest:
    powershell -NoProfile -ExecutionPolicy Bypass -File C:\GanttIt\tools\build-deploy.ps1 -Zip C:\GanttIt\stage\gantt-it-src.zip -Years 2025,2026,2027 -Deploy

  Layout produced:
    C:\GanttIt\src\gantt-it\                                  expanded source
    src\GanttIt.Revit\bin\<Flavor> R2x\net*-windows\         build output
    %AppData%\Autodesk\Revit\Addins\<year>\GanttIt.addin     manifest (Assembly: GanttIt\GanttIt.Revit.dll)
    %AppData%\Autodesk\Revit\Addins\<year>\GanttIt\          DLLs and icons
    C:\GanttIt\logs\build-gantt-it-<year>.log                build log
  Arguments avoid spaces on purpose: `sc vm exec` goes through cmd.exe, so -Years maps to the
  "Debug R25"/"Debug R26"/"Debug R27" configurations here. Revit locks loaded DLLs: stop Revit before deploying.
#>
param(
  [Parameter(Mandatory)][string]$Zip,
  [string]$Years = '2026',   # comma-separated: -File passes arrays as one string
  [string]$Flavor = 'Debug',
  [switch]$Deploy
)
$ErrorActionPreference = 'Stop'
$src = 'C:\GanttIt\src\gantt-it'
$logs = 'C:\GanttIt\logs'
New-Item -ItemType Directory -Force $logs | Out-Null
if (Test-Path $src) { Remove-Item $src -Recurse -Force }
Expand-Archive -Path $Zip -DestinationPath $src -Force
$proj = Join-Path $src 'src\GanttIt.Revit\GanttIt.Revit.csproj'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

foreach ($year in ($Years -split ',' | ForEach-Object { [int]$_.Trim() })) {
  $tag, $tfm = switch ($year) { 2025 { 'R25', 'net8.0-windows' } 2026 { 'R26', 'net8.0-windows' } 2027 { 'R27', 'net10.0-windows' } default { throw "unsupported year $year" } }
  $cfg = "$Flavor $tag"
  $log = Join-Path $logs "build-gantt-it-$year.log"
  $sw = [Diagnostics.Stopwatch]::StartNew()
  & dotnet build $proj -c $cfg -nologo -v:minimal *> $log
  $code = $LASTEXITCODE
  Write-Output ("build [{0}] exit={1} in {2:n1}s (log {3})" -f $cfg, $code, $sw.Elapsed.TotalSeconds, $log)
  Get-Content $log | Select-String -Pattern 'error|warn' | Select-Object -First 30 | ForEach-Object { Write-Output ("  " + $_.Line) }
  if ($code -ne 0) { exit $code }

  if ($Deploy) {
    if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Output 'Revit is running; stop it first'; exit 2 }
    $out = Join-Path $src "src\GanttIt.Revit\bin\$cfg\$tfm"
    $addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$year"
    $dest = Join-Path $addins 'GanttIt'
    if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
    New-Item -ItemType Directory -Force $dest | Out-Null
    Copy-Item (Join-Path $out '*') $dest -Recurse -Force
    Move-Item (Join-Path $dest 'GanttIt.addin') (Join-Path $addins 'GanttIt.addin') -Force
    $dll = Get-Item (Join-Path $dest 'GanttIt.Revit.dll')
    Write-Output ("deployed {0} -> {1} ({2:u}, {3} bytes)" -f $cfg, $dest, $dll.LastWriteTime, $dll.Length)
  }
}
