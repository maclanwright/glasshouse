# UI inspector capture (22 Oct 2026): open a piece of Windows' own interface, list every part UI Automation can see
# (its type, Windows class name, name and on-screen box) and take a full screenshot. The server
# (server.py) crops the picture to the parts and draws a clickable outline per part, so you can point at a part
# and say how it should look. Writes <Out>\raw.json and <Out>\full.png. Runs in the signed-in desktop.
# Surfaces: start | search | explorer | screen (whatever is in front, as it is).
param([Parameter(Mandatory)][string]$Surface, [Parameter(Mandatory)][string]$Out, [string]$Query = 'settings', [string]$Hover = '',
      [string]$HoverAt = '')   # "x,y" in screen pixels, for parts UI Automation can't see (search results)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class UiK { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int w, int hh, uint f);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
 [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e); }
"@
[void][UiK]::SetProcessDPIAware()
$ExplorerBefore = @(Get-Process explorer -EA SilentlyContinue | ForEach-Object Id)
New-Item -ItemType Directory -Force $Out | Out-Null
# Positions follow the main screen, whatever its size
$PS = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$ParkX = [int]($PS.X + $PS.Width * 0.17); $ParkY = [int]($PS.Y + $PS.Height * 0.28)
$EX = [int]($PS.X + $PS.Width * 0.14); $EY = [int]($PS.Y + $PS.Height * 0.10); $EW = [int]($PS.Width * 0.72); $EH = [int]($PS.Height * 0.77)
function ScreenShot($file) {
  $vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
  $bmp = New-Object System.Drawing.Bitmap $vs.Width, $vs.Height
  $g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($vs.X, $vs.Y, 0, 0, $bmp.Size)
  $bmp.Save($file, [System.Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
}
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
function Tap($vk, $sc, $ext = 0) { [UiK]::keybd_event($vk, $sc, $ext, [UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [UiK]::keybd_event($vk, $sc, $ext -bor 2, [UIntPtr]::Zero) }
function WinKey { Tap 0x5B 0x5B 1 }
function Esc { Tap 0x1B 0x01 }
# Start and search are found by the program that draws them (window titles differ between Windows builds),
# retrying for a few seconds while they open
function ProcWindow([string]$proc) {
  for ($n = 0; $n -lt 15; $n++) {
    $ids = @(Get-Process $proc -EA SilentlyContinue | ForEach-Object Id)
    foreach ($w in $AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition)) {
      $c = $w.Current
      if ($ids -contains $c.ProcessId -and -not $c.BoundingRectangle.IsEmpty -and $c.BoundingRectangle.Width -gt 100 -and -not $c.IsOffscreen) { return $w }
    }
    Start-Sleep -Milliseconds 200
  }
  return $null
}
# Opening Start (or typing in it) puts Windows' keyboard focus in its search box: climb from the focused part up to
# its top-level window. This doesn't depend on window names or how UI Automation lists Start.
function FocusWindow([string[]]$procs) {
  $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
  for ($n = 0; $n -lt 15; $n++) {
    try {
      $el = $AE::FocusedElement
      $p = (Get-Process -Id $el.Current.ProcessId -EA SilentlyContinue).ProcessName
      if ($procs -contains $p) {
        $parent = $walker.GetParent($el)
        while ($parent -and -not $parent.Equals($AE::RootElement)) { $el = $parent; $parent = $walker.GetParent($el) }
        return $el
      }
    } catch {}
    Start-Sleep -Milliseconds 200
  }
  return $null
}
function OnScreen { ($AE::RootElement.FindAll($TS::Children, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object {
    $c = $_.Current; $p = (Get-Process -Id $c.ProcessId -EA SilentlyContinue).ProcessName; "$p '$($c.Name)'" } | Select-Object -First 12) -join '; ' }
$roots = @(); $cleanup = $null; $extra = $null
switch ($Surface) {
  'start' {
    # On this Windows build Start is two windows: SearchHost draws the search box (it gets the keyboard focus) and
    # StartMenuExperienceHost the menu itself, which UI Automation doesn't list; Tab moves the focus into the menu.
    WinKey; Start-Sleep -Milliseconds 1200
    $box = FocusWindow 'SearchHost', 'StartMenuExperienceHost'
    Tap 0x09 0x0F; Start-Sleep -Milliseconds 500
    $menu = FocusWindow 'StartMenuExperienceHost'
    # Shift+Tab puts the focus back in the search box, so no keyboard-focus ring is left on a menu part
    [UiK]::keybd_event(0x10, 0x2A, 0, [UIntPtr]::Zero); Tap 0x09 0x0F; [UiK]::keybd_event(0x10, 0x2A, 2, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 400
    $roots = @($menu, $box)
    $cleanup = { Esc }
  }
  'search' {
    WinKey; Start-Sleep -Milliseconds 1200
    [System.Windows.Forms.SendKeys]::SendWait($Query); Start-Sleep -Milliseconds 2000
    $roots = @(FocusWindow 'SearchHost', 'StartMenuExperienceHost')
    if (-not $roots[0]) { $roots = @((ProcWindow 'SearchHost'), (ProcWindow 'StartMenuExperienceHost')) }
    $cleanup = { Esc; Start-Sleep -Milliseconds 300; Esc }
  }
  { $_ -in 'explorer', 'explorer-menu' } {
    $shell = New-Object -ComObject Shell.Application
    $before = @($shell.Windows() | ForEach-Object { $_.HWND })
    Start-Process explorer.exe 'shell:MyComputerFolder'
    $w = $null; for ($n = 0; $n -lt 30 -and -not $w; $n++) { Start-Sleep -Milliseconds 300; $w = $shell.Windows() | Where-Object { $before -notcontains $_.HWND } | Select-Object -First 1 }
    $h = [IntPtr]$w.HWND
    [void][UiK]::SetWindowPos($h, [IntPtr]::Zero, $EX, $EY, $EW, $EH, 0x0040); [void][UiK]::SetForegroundWindow($h); Start-Sleep 2
    $roots = @($AE::FromHandle($h))
    $cleanup = { $w.Quit() }.GetNewClosure()
    if ($Surface -eq 'explorer-menu') {
      # right-click empty space in the file area; the menu takes the keyboard focus, which leads to its window
      [void][UiK]::SetCursorPos([int]($EX + $EW * 0.62), [int]($EY + $EH * 0.81)); Start-Sleep -Milliseconds 300
      [UiK]::mouse_event(8, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [UiK]::mouse_event(16, 0, 0, 0, [UIntPtr]::Zero)
      Start-Sleep -Milliseconds 1500
      $menu = FocusWindow 'explorer'
      if ($menu -and -not $menu.Current.NativeWindowHandle.Equals([int]$h)) { $roots = @($menu, $roots[0]) }
      $cleanup = { Esc; Start-Sleep -Milliseconds 300; $w.Quit() }.GetNewClosure()
    }
  }
  'dock' {
    # reveal the auto-hidden dock and hover Docker so its window preview opens (the preview itself is drawn by
    # Windows outside UI Automation, so it is added to the picture as an extra area above the button)
    [void][UiK]::SetCursorPos([int]($PS.X + $PS.Width / 2), $PS.Bottom - 1); [UiK]::mouse_event(1, 0, -1, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 1200
    $bar = $AE::RootElement.FindFirst($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::ClassNameProperty, 'Shell_TrayWnd')))
    $btn = $null
    if ($bar) { $btn = $bar.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | Where-Object { $_.Current.Name -match 'running window' -and $_.Current.Name -notmatch '^Parsec' } | Select-Object -First 1 }
    if ($btn) {
      foreach ($i in 1..3) { $r = $btn.Current.BoundingRectangle
        [void][UiK]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2 + 8)); [UiK]::mouse_event(1, 1, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 500 }
      Start-Sleep -Milliseconds 1500
      $r = $btn.Current.BoundingRectangle
      $extra = [ordered]@{ x = [int]($r.X + $r.Width / 2 - 520); y = [int]($r.Y - 560); w = 1040; h = 560 }
    }
    $roots = @($bar)
    $cleanup = { [void][UiK]::SetCursorPos($ParkX, $ParkY) }
  }
  'screen' {
    $roots = @($AE::FromHandle([UiK]::GetForegroundWindow()))
  }
  default { throw "unknown surface $Surface" }
}
$roots = @($roots | Where-Object { $_ })
if (-not $roots) { $seen = OnScreen; if ($cleanup) { & $cleanup }; [Console]::Error.WriteLine("Couldn't find the $Surface window. On screen were: $seen"); exit 2 }
# walk every root's control tree (depth and count capped so a busy Explorer folder stays quick)
# search results are a web page inside the window, only visible in UI Automation's full (raw) view
$walker = if ($Surface -eq 'search') { [System.Windows.Automation.TreeWalker]::RawViewWalker } else { [System.Windows.Automation.TreeWalker]::ControlViewWalker }
$parts = New-Object System.Collections.ArrayList
function Walk($el, $depth, $parentId) {
  if ($parts.Count -ge 700 -or $depth -gt 30) { return }
  try { $c = $el.Current } catch { return }
  $r = $c.BoundingRectangle
  $id = $parts.Count
  if (-not $r.IsEmpty -and $r.Width -ge 2 -and $r.Height -ge 2 -and -not $c.IsOffscreen) {
    [void]$parts.Add([ordered]@{ id = $id; parent = $parentId; depth = $depth
      type = ($c.ControlType.ProgrammaticName -replace '^ControlType\.', ''); cls = $c.ClassName; name = $c.Name; aid = $c.AutomationId
      x = [int]$r.X; y = [int]$r.Y; w = [int]$r.Width; h = [int]$r.Height })
  } else { $id = $parentId }
  $child = $walker.GetFirstChild($el)
  while ($child -and $parts.Count -lt 700) { Walk $child ($depth + 1) $id; $child = $walker.GetNextSibling($child) }
}
foreach ($root in $roots) { Walk $root 0 -1 }
# optionally hold the mouse over one part (by name) so its hover look is in the picture
if ($Hover) {
  $t = $parts | Where-Object { $_.name -like "*$Hover*" } | Select-Object -First 1
  if ($t) { $HoverAt = "$([int]($t.x + $t.w / 2)),$([int]($t.y + $t.h / 2))" }
}
if ($HoverAt) {
  # glide in from the left in small steps, like a real mouse, so every kind of Windows part notices the hover
  $xy = $HoverAt -split ','; $hx = [int]$xy[0]; $hy = [int]$xy[1]
  [void][UiK]::SetCursorPos($hx - 40, $hy)
  foreach ($i in 1..8) { [UiK]::mouse_event(1, 5, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 35 }
  [void][UiK]::SetCursorPos($hx, $hy); Start-Sleep -Milliseconds 1100
}
ScreenShot "$Out\full.png"
if ($cleanup) { & $cleanup }
# File Explorer windows opened from a script run in their own helper process ("explorer.exe /factory,...
# -Embedding"), which stays behind after the window closes: 24 of them (3.9 GB) had piled up by 22 Oct 2026.
Start-Sleep -Milliseconds 800
Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object { $ExplorerBefore -notcontains $_.ProcessId -and $_.CommandLine -match '/factory' } |
  ForEach-Object { Stop-Process -Id $_.ProcessId -Force -EA SilentlyContinue }
$vs = [System.Windows.Forms.SystemInformation]::VirtualScreen
@{ surface = $Surface; query = $Query; time = (Get-Date -Format s); vx = $vs.X; vy = $vs.Y; vw = $vs.Width; vh = $vs.Height
   parts = $parts; extra = $extra } | ConvertTo-Json -Depth 5 | Set-Content "$Out\raw.json" -Encoding UTF8
"captured $($parts.Count) parts"
