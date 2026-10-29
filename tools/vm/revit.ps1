<#
  revit.ps1 — guest side (W10C-Testing). Drive Revit and the Gantt-It Self-Test from `./sc vm exec`.

  Revit must run in the interactive desktop session, but `sc vm exec` runs in a non-interactive
  SSH session. -Action launch therefore registers a one-shot scheduled task with an Interactive
  principal for the auto-logged-on user; Revit appears on the console desktop, where the Windows
  MCP and `sc vm capture` can see it.

  Actions:
    prep     -Model <work .rvt> -From <fixture .rvt>    copy a fixture to a disposable work copy
                                                        (refuses to write into C:\GanttIt\fixtures)
    launch   -Year 2025|2026|2027 -Model <.rvt> [-Phase 1|2] [-NoSave] [-RevitArgs '/language FRA']
             -RevitArgs is passed to Revit.exe before the model path, e.g. '/language DEU' to start
             Revit in another UI language (FRA, DEU, ESP, ITA, ...; A24).
             With -Phase, arms the autorun Self-Test: writes %TEMP%\GanttIt\autorun.json; the
             Debug add-in consumes it on its first idle tick with the document open and writes
             selftest-<year>-<model stem>-<phase>.json plus a .done marker.
    request  -Phase undo-check                          write selftest-request.json for the next
                                                        click on the "Gantt-It Self-Test" ribbon button
             -Phase place-sheets|a8-check -Chart <name> A8 helpers: place the chart on 2 new sheets;
                                                        after the UI Delete, check what is left
    wait     -Marker <path> [-TimeoutSec 100]           poll until the file exists (or Revit exits);
                                                        keep < 120 s (the exec limit)
    stop                                                kill Revit (work copies only; no save)
    status                                              Revit processes, window titles, report folder
    clean                                               delete old reports, images and markers

  Notes (Phase 0, 2026-10-28):
    * Reports live in %LOCALAPPDATA%\Temp\GanttIt (= this script's %TEMP%\GanttIt). Inside Revit,
      %TEMP% is a per-session GUID subfolder, so the add-in never uses Path.GetTempPath().
    * The unsigned add-in prompt still has to be answered (Windows MCP: WaitFor "Always Load", click
      it). Revit keeps the choice only on a clean exit, so expect it after each deploy and each kill.
    * Revit 2027 shows a "Hardware Acceleration disabled" dialog at startup; the Debug add-in's
      DialogBoxShowing handler dismisses it while an autorun is armed.

  Example (host):
    ./sc vm exec --command-file launch.cmd   # powershell -NoProfile -ExecutionPolicy Bypass -File C:\GanttIt\tools\revit.ps1 -Action launch -Year 2026 -Model C:\GanttIt\work\metric-26.rvt -Phase 1
#>
param(
  [Parameter(Mandatory)][ValidateSet('prep', 'launch', 'request', 'wait', 'stop', 'status', 'clean')][string]$Action,
  [int]$Year = 2026,
  [string]$Model,
  [string]$From,
  [string]$Phase,
  [switch]$NoSave,
  [string]$Marker,
  [string]$Chart,
  [string]$RevitArgs,   # extra Revit.exe arguments, e.g. '/language FRA'
  [int]$TimeoutSec = 100   # `sc vm exec` itself times out at 120 s: poll in short calls
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $env:TEMP 'GanttIt'
New-Item -ItemType Directory -Force $out | Out-Null

function ArgsJson([string]$phase, [bool]$quiet) {
  $save = if ($NoSave) { 'false' } else { 'true' }
  $q = if ($quiet) { ',"quiet":true' } else { '' }
  $c = if ($Chart) { ',"chart":' + (ConvertTo-Json $Chart) } else { '' }
  return '{"phase":"' + $phase + '","save":' + $save + $q + $c + '}'
}

switch ($Action) {
  'prep' {
    if ($Model -match '\\fixtures\\') { throw "refusing to write into the fixtures folder: $Model" }
    New-Item -ItemType Directory -Force (Split-Path $Model -Parent) | Out-Null
    Copy-Item $From $Model -Force
    Write-Output "copied $From -> $Model"
  }
  'launch' {
    if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Output 'Revit already running; stop it first'; exit 2 }
    if ($Phase) {
      $json = '{"action":"selftest","args":' + (ArgsJson $Phase $false) + '}'
      [IO.File]::WriteAllText((Join-Path $out 'autorun.json'), $json)
      $stem = [IO.Path]::GetFileNameWithoutExtension($Model)
      Remove-Item (Join-Path $out "selftest-$Year-$stem-$Phase.json*") -Force -ErrorAction SilentlyContinue
      Write-Output "autorun armed: $json"
      Write-Output ("marker: " + (Join-Path $out "selftest-$Year-$stem-$Phase.json.done"))
    }
    $exe = "C:\Program Files\Autodesk\Revit $Year\Revit.exe"
    if (-not (Test-Path $exe)) { throw "not found: $exe" }
    $task = 'GanttIt-Revit'
    $argList = @()
    if ($RevitArgs) { $argList += $RevitArgs }
    if ($Model) { $argList += ('"' + $Model + '"') }
    $a = if ($argList) { New-ScheduledTaskAction -Execute $exe -Argument ($argList -join ' ') } else { New-ScheduledTaskAction -Execute $exe }
    $p = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $task -Action $a -Principal $p -Settings $s -Force | Out-Null
    Start-ScheduledTask -TaskName $task
    Start-Sleep -Seconds 3
    $proc = Get-Process Revit -ErrorAction SilentlyContinue
    Write-Output ("launched Revit {0} args='{1}' model={2} pid={3} at {4:u}" -f $Year, $RevitArgs, $Model, ($proc.Id -join ','), (Get-Date))
  }
  'request' {
    $json = '{"args":' + (ArgsJson $Phase $true) + '}'
    [IO.File]::WriteAllText((Join-Path $out 'selftest-request.json'), $json)
    Write-Output "request written: $json (click the 'Gantt-It Self-Test' ribbon button)"
  }
  'wait' {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $Marker)) {
      if ($sw.Elapsed.TotalSeconds -gt $TimeoutSec) { Write-Output "timeout after $TimeoutSec s waiting for $Marker"; exit 3 }
      if (-not (Get-Process Revit -ErrorAction SilentlyContinue)) { Write-Output "Revit is not running (waited $([int]$sw.Elapsed.TotalSeconds) s)"; exit 4 }
      Start-Sleep -Seconds 5
    }
    Write-Output ("marker present after {0:n0} s: {1}" -f $sw.Elapsed.TotalSeconds, $Marker)
    $report = $Marker -replace '\.done$', ''
    if (Test-Path $report) {
      $r = Get-Content $report -Raw | ConvertFrom-Json
      Write-Output ("overall={0} passed={1} failed={2}" -f $r.overall, $r.summary.passed, $r.summary.failed)
      $r.checks | Where-Object { $_.result -ne 'PASS' } | Select-Object -First 15 | ForEach-Object { Write-Output ("  FAIL [{0}] {1}" -f $_.criterion, $_.name) }
    }
  }
  'stop' {
    Get-Process Revit -ErrorAction SilentlyContinue | Stop-Process -Force
    for ($i = 0; $i -lt 30 -and (Get-Process Revit -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Seconds 1 }
    Get-Process senddmp, RevitAccelerator -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Write-Output ("Revit running after stop: {0}" -f [bool](Get-Process Revit -ErrorAction SilentlyContinue))
  }
  'status' {
    Get-Process Revit -ErrorAction SilentlyContinue | Select-Object Id, StartTime, MainWindowTitle, @{n = 'WS_MB'; e = { [int]($_.WorkingSet64 / 1MB) } } | Format-Table -AutoSize | Out-String -Width 200
    Get-ChildItem $out -ErrorAction SilentlyContinue | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize | Out-String -Width 200
  }
  'clean' {
    Get-ChildItem $out -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(selftest|chart-|autorun)' } | Remove-Item -Force
    Write-Output "cleaned $out"
  }
}
