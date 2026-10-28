<#
  revit.ps1 — guest side (W10C-Testing). Drive Revit from `./sc vm exec` without the GUI.

  Revit must run in the interactive desktop session, but `sc vm exec` runs in a non-interactive
  SSH session. -Action launch therefore registers a one-shot scheduled task with an Interactive
  principal for the current (auto-logged-on) user and starts it; Revit appears on the console
  desktop, where the Windows MCP and `sc vm capture` can see it.

  Actions:
    prep    -Model <work .rvt> -From <fixture .rvt>   copy a fixture to a disposable work copy
    launch  -Year 2026|2027 [-Model <.rvt>] [-Autorun <action>] [-AutorunArgs <file.json>]
            Writes %TEMP%\GanttIt\autorun.json = { "action": <action>, "args": <file contents> }
            when -Autorun is given; the add-in consumes it on its first Idling tick with a document.
    wait    -Marker <path> [-TimeoutSec 100]           poll until the file exists (or Revit exits); keep < 120 s (exec limit)
    stop                                               kill Revit (work copies only; no save)
    status                                             Revit processes + window titles + %TEMP%\GanttIt listing

  Notes (Phase 0, 2026-10-28):
    * Output lives in %LOCALAPPDATA%\Temp\GanttIt (= this script's %TEMP%\GanttIt). Inside Revit,
      %TEMP% is a per-session GUID subfolder, so the add-in must NOT use Path.GetTempPath().
    * The unsigned add-in prompt still has to be answered (Windows MCP: WaitFor "Always Load", click it).
      Revit persists the choice (HKCU\...\Autodesk Revit <year>\CodeSigning\<ClientId>=1) only on a clean
      exit; pre-seeding that value did NOT suppress the prompt in 2027.
    * 'stop' kills Revit: fine for disposable work copies, but it loses an unsaved Always Load choice.

  Example (host):
    ./sc vm exec -- powershell -NoProfile -ExecutionPolicy Bypass -File C:\GanttIt\tools\revit.ps1 -Action launch -Year 2026 -Model C:\GanttIt\work\m26.rvt -Autorun spike
#>
param(
  [Parameter(Mandatory)][ValidateSet('prep', 'launch', 'wait', 'stop', 'status')][string]$Action,
  [int]$Year = 2026,
  [string]$Model,
  [string]$From,
  [string]$Autorun,
  [string]$AutorunArgs,
  [string]$Marker,
  [int]$TimeoutSec = 100   # `sc vm exec` itself times out at 120 s: poll in short calls
)
$ErrorActionPreference = 'Stop'
$out = Join-Path $env:TEMP 'GanttIt'
New-Item -ItemType Directory -Force $out | Out-Null

switch ($Action) {
  'prep' {
    New-Item -ItemType Directory -Force (Split-Path $Model -Parent) | Out-Null
    Copy-Item $From $Model -Force
    Write-Output "copied $From -> $Model"
  }
  'launch' {
    if (Get-Process Revit -ErrorAction SilentlyContinue) { Write-Output 'Revit already running; stop it first'; exit 2 }
    if ($Autorun) {
      $argsJson = if ($AutorunArgs) { Get-Content $AutorunArgs -Raw } else { '{}' }
      $json = '{"action":"' + $Autorun + '","args":' + $argsJson + '}'
      [IO.File]::WriteAllText((Join-Path $out 'autorun.json'), $json)
      Remove-Item (Join-Path $out "$Autorun-$Year.json*") -Force -ErrorAction SilentlyContinue
      Write-Output "autorun armed: $json"
    }
    $exe = "C:\Program Files\Autodesk\Revit $Year\Revit.exe"
    if (-not (Test-Path $exe)) { throw "not found: $exe" }
    $task = 'GanttIt-Revit'
    $a = if ($Model) { New-ScheduledTaskAction -Execute $exe -Argument ('"' + $Model + '"') } else { New-ScheduledTaskAction -Execute $exe }
    $p = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
    $s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName $task -Action $a -Principal $p -Settings $s -Force | Out-Null
    Start-ScheduledTask -TaskName $task
    Start-Sleep -Seconds 3
    $proc = Get-Process Revit -ErrorAction SilentlyContinue
    Write-Output ("launched Revit {0} model={1} pid={2} at {3:u}" -f $Year, $Model, ($proc.Id -join ','), (Get-Date))
  }
  'wait' {
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path $Marker)) {
      if ($sw.Elapsed.TotalSeconds -gt $TimeoutSec) { Write-Output "timeout after $TimeoutSec s waiting for $Marker"; exit 3 }
      if (-not (Get-Process Revit -ErrorAction SilentlyContinue)) { Write-Output "Revit is not running (waited $([int]$sw.Elapsed.TotalSeconds) s)"; exit 4 }
      Start-Sleep -Seconds 5
    }
    Write-Output ("marker present after {0:n0} s: {1}" -f $sw.Elapsed.TotalSeconds, $Marker)
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
}
