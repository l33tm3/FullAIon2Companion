# Desarrollo local: cierra cualquier FullAIon2Companion abierto (cierre limpio, guarda datos), compila en desktop/build-dev y lo abre.
# No genera instalador ni toca la versión instalada.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$out = Join-Path $root 'desktop/build-dev'
Add-Type -Name DevWin -Namespace Companion -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h, int c);
[DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
public delegate bool CB(IntPtr h, IntPtr s);
[DllImport("user32.dll")] public static extern bool EnumWindows(CB cb, IntPtr s);
[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, System.Text.StringBuilder b, int n);
public static System.Collections.Generic.List<IntPtr> Of(uint pid) { var l = new System.Collections.Generic.List<IntPtr>(); EnumWindows((h, s) => { uint o; GetWindowThreadProcessId(h, out o); if (o == pid) l.Add(h); return true; }, IntPtr.Zero); return l; }
'@ -ErrorAction SilentlyContinue
dotnet build (Join-Path $root 'desktop/FullAIon2Companion/FullAIon2Companion.csproj') -c Release -o (Join-Path $root 'desktop/build-dev-next') -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed: the running app was left open' }
foreach ($p in Get-Process FullAIon2Companion -ErrorAction SilentlyContinue) {
  # A visible panel only minimizes on the first close; the next close quits. Try a few times.
  for ($try = 0; $try -lt 3 -and -not $p.HasExited; $try++) {
    foreach ($h in [Companion.DevWin]::Of([uint32]$p.Id)) {
        $b = New-Object System.Text.StringBuilder 200; [void][Companion.DevWin]::GetWindowTextW($h, $b, 200)
        # The main panel: minimized, WM_CLOSE quits like "Salir" (flushes settings and collections).
        if ($b.ToString() -match '^FullAIon2Companion\W+\d') { [void][Companion.DevWin]::ShowWindowAsync($h, 6); Start-Sleep -Milliseconds 600; [void][Companion.DevWin]::PostMessage($h, 0x10, [IntPtr]::Zero, [IntPtr]::Zero) }
    }
    [void]$p.WaitForExit(4000)
  }
  if (-not $p.HasExited) { throw "FullAIon2Companion $($p.Id) did not close" }
}
robocopy (Join-Path $root 'desktop/build-dev-next') $out /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
$app = Start-Process (Join-Path $out 'FullAIon2Companion.exe') -WorkingDirectory $out -PassThru
"Dev build running: pid $($app.Id) from $out"
