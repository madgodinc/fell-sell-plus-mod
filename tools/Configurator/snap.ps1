# Dev helper: launch the configurator, optionally click points (screen coords), screenshot, close it (own PID only).
param([string]$Out, [string]$Clicks = "", [int]$Wait = 3, [string]$AppArgs = "", [switch]$Full)
$exe = Join-Path $PSScriptRoot "bin\Release\net8.0-windows\FellSellPlusConfigurator.exe"
$p = if ($AppArgs) { Start-Process $exe -ArgumentList $AppArgs -PassThru } else { Start-Process $exe -PassThru }
Start-Sleep $Wait
Add-Type -Namespace W -Name U -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern bool MoveWindow(System.IntPtr h, int x, int y, int w, int hh, bool r);
[DllImport("user32.dll")] public static extern void mouse_event(uint f, uint x, uint y, uint d, System.IntPtr e);
'@
$p.Refresh(); [W.U]::MoveWindow($p.MainWindowHandle, 100, 60, 1860, 1230, $true) | Out-Null; [W.U]::SetForegroundWindow($p.MainWindowHandle) | Out-Null; Start-Sleep -Milliseconds 400
foreach ($c in ($Clicks -split ';' | Where-Object { $_ })) {
    $xy = $c -split ','
    [W.U]::SetCursorPos([int]$xy[0], [int]$xy[1]) | Out-Null; Start-Sleep -Milliseconds 120
    [W.U]::mouse_event(2, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60; [W.U]::mouse_event(4, 0, 0, 0, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 700
}
& (Join-Path $PSScriptRoot "..\shot.ps1") -Out $Out -Full:$Full
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
