param([string]$Version='1.0.3')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$installRoot=[IO.Path]::GetFullPath('D:/Users/nanos/AppData/Local/Programs/FullAIon2Companion')
if($installRoot -ne 'D:\Users\nanos\AppData\Local\Programs\FullAIon2Companion'){throw 'Unexpected installation destination'}
$packageRoot=Join-Path $projectRoot "desktop/release/$Version-final"
$installer=Join-Path $packageRoot "FullAIon2Companion-$Version-setup.exe"
$manifest=Get-Content -LiteralPath (Join-Path $packageRoot 'release.json') -Raw | ConvertFrom-Json
if($manifest.version -ne $Version -or (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $manifest.sha256){throw 'Installer manifest mismatch'}
$installedExe=Join-Path $installRoot 'FullAIon2Companion.exe'
$allowedExecutables=@($installedExe,(Join-Path $projectRoot 'desktop/build-vw2/FullAIon2Companion.exe'),(Join-Path $projectRoot 'desktop/build-dev/FullAIon2Companion.exe'),(Join-Path $projectRoot 'desktop/build-dev/FullAIon2Companion.exe')) | ForEach-Object {[IO.Path]::GetFullPath($_)}
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
public static class CompanionInstallWindow {
 public delegate bool EnumCallback(IntPtr h,IntPtr state);
 [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback,IntPtr state);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h,out uint process);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint message,IntPtr w,IntPtr l);
 public static IntPtr[] Windows(int process){var found=new List<IntPtr>();EnumWindows((h,s)=>{uint owner;GetWindowThreadProcessId(h,out owner);if(owner==process)found.Add(h);return true;},IntPtr.Zero);return found.ToArray();}
 [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr h,int command);
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h,IntPtr after,int x,int y,int cx,int cy,uint flags);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
}
'@
$previous=@(Get-Process -Name FullAIon2Companion -ErrorAction SilentlyContinue)
foreach($app in $previous){
 if([IO.Path]::GetFullPath($app.Path) -notin $allowedExecutables){throw "Unrecognized Companion instance: $($app.Id); leave it unchanged"}
 for($attempt=0;$attempt -lt 4 -and -not $app.HasExited;$attempt++){
  $app.Refresh()
  $windows=[CompanionInstallWindow]::Windows($app.Id)
  foreach($window in $windows){
   # MainWindow handles taskbar-close as exit when minimized, flushing settings and collections.
   [void][CompanionInstallWindow]::ShowWindowAsync($window,6)
   Start-Sleep -Milliseconds 200
   [void][CompanionInstallWindow]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
  }
  [void]$app.WaitForExit(2500)
 }
 if(-not $app.HasExited){throw "Companion $($app.Id) did not close; installation stopped"}
}
# Back up user settings after the old processes have flushed, without exporting the device credential.
$dataRoot=Join-Path $env:LOCALAPPDATA 'FullAIon2Companion'
$backupRoot=Join-Path $projectRoot ("output/install-$Version-backup")
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
$saved=@()
foreach($file in Get-ChildItem -LiteralPath $dataRoot -File -Filter '*.json'){
 if($file.Name -in @('settings.json','pets.json','pet-collection.json','pet-tracker.json','pet-day.json') -or $file.Name -like 'pet-collection-*.json'){
  Copy-Item -LiteralPath $file.FullName -Destination $backupRoot -Force
  $saved+=@{file=$file.Name;sha256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()}
 }
}
$installerLog=Join-Path $projectRoot "output/install-$Version.log"
$arguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-',('/DIR="'+$installRoot+'"'),('/LOG="'+$installerLog+'"'))
$setup=Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
if($setup.ExitCode -ne 0){throw "Installer failed: exit $($setup.ExitCode). See $installerLog"}
$installedDll=Join-Path $installRoot 'FullAIon2Companion.dll'
$sourceDll=Join-Path $packageRoot 'app/FullAIon2Companion.dll'
if((Get-FileHash -LiteralPath $installedDll).Hash -ne (Get-FileHash -LiteralPath $sourceDll).Hash){throw 'Installed assembly mismatch'}
if([Diagnostics.FileVersionInfo]::GetVersionInfo($installedDll).ProductVersion -notlike "$Version*"){throw 'Installed version mismatch'}
foreach($file in $saved){if((Get-FileHash -LiteralPath (Join-Path $dataRoot $file.file)).Hash.ToLowerInvariant() -ne $file.sha256){throw "Installation changed user data: $($file.file)"}}
$newApp=Start-Process -FilePath $installedExe -WorkingDirectory $installRoot -WindowStyle Normal -PassThru
for($attempt=0;$attempt -lt 40;$attempt++){
 Start-Sleep -Milliseconds 250; $newApp.Refresh()
 if($newApp.HasExited){throw 'Installed application exited during startup'}
 if($newApp.MainWindowHandle -ne [IntPtr]::Zero){break}
}
$handle=$newApp.MainWindowHandle
if($handle -eq [IntPtr]::Zero){throw 'No application window appeared'}
[void][CompanionInstallWindow]::ShowWindowAsync($handle,9)
Start-Sleep -Milliseconds 500
[void][CompanionInstallWindow]::SetWindowPos($handle,[IntPtr](-1),0,0,0,0,0x0043)
[void][CompanionInstallWindow]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 500
$report=@{ok=$true;version=$Version;installedPath=$installedExe;processId=$newApp.Id;windowTitle=$newApp.MainWindowTitle;visible=[CompanionInstallWindow]::IsWindowVisible($handle);minimized=[CompanionInstallWindow]::IsIconic($handle);foreground=([CompanionInstallWindow]::GetForegroundWindow() -eq $handle);preservedUserFiles=$saved.Count;installerExit=$setup.ExitCode;installedAssemblySha256=(Get-FileHash -LiteralPath $installedDll -Algorithm SHA256).Hash.ToLowerInvariant();previousProcessesClosed=@($previous | ForEach-Object {$_.Id});checkedAt=[DateTime]::UtcNow.ToString('o')}
$report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $projectRoot "output/desktop-install-$Version.json") -Encoding utf8
$report | ConvertTo-Json

