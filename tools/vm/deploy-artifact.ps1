<#
  deploy-artifact.ps1 — guest side (W10C-Testing). Install a staged Gantt-It build as a dev add-in.

  The CI build job uploads GanttIt-Debug-R25 / -R26 / -R27 laid out exactly like
  %AppData%\Autodesk\Revit\Addins\<year>\ (GanttIt.addin + GanttIt\). On the host:
      gh run download <run-id> -n GanttIt-Debug-R26 -D .sc-state/local/vm-stage/r26
      (cd .sc-state/local/vm-stage/r26 && zip -qr ../GanttIt-Debug-R26.zip .)
      ./sc vm push <abs>/.sc-state/local/vm-stage/GanttIt-Debug-R26.zip C:\GanttIt\stage\GanttIt-Debug-R26.zip
  then here:
      powershell -NoProfile -ExecutionPolicy Bypass -File C:\GanttIt\tools\deploy-artifact.ps1 -Zip C:\GanttIt\stage\GanttIt-Debug-R26.zip -Year 2026

  Revit locks loaded DLLs: stop Revit first (revit.ps1 -Action stop). Each deploy re-triggers the
  unsigned add-in prompt on the next launch (D14).
#>
param(
  [Parameter(Mandatory)][string]$Zip,
  [Parameter(Mandatory)][int]$Year
)
$ErrorActionPreference = 'Stop'
if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Output 'Revit is running; stop it first'; exit 2 }

$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$Year"
$dest = Join-Path $addins 'GanttIt'
$tmp = Join-Path $env:TEMP ("gantt-it-deploy-" + [guid]::NewGuid().ToString('N'))
Expand-Archive -Path $Zip -DestinationPath $tmp -Force
if (-not (Test-Path (Join-Path $tmp 'GanttIt.addin'))) { throw "not an add-in artifact (no GanttIt.addin at the top): $Zip" }

New-Item -ItemType Directory -Force $addins | Out-Null
if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
Copy-Item (Join-Path $tmp 'GanttIt') $dest -Recurse -Force
Copy-Item (Join-Path $tmp 'GanttIt.addin') (Join-Path $addins 'GanttIt.addin') -Force
Remove-Item $tmp -Recurse -Force

$dll = Get-Item (Join-Path $dest 'GanttIt.Revit.dll')
$version = (Get-Item $dll).VersionInfo.ProductVersion
Write-Output ("deployed {0} -> {1} ({2:u}, {3} bytes, version {4})" -f $Zip, $dest, $dll.LastWriteTime, $dll.Length, $version)
