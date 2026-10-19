param([string]$Version='1.0.3', [string]$Output='desktop/release/1.0.3-final')
$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$releaseRoot=[IO.Path]::GetFullPath((Join-Path $projectRoot $Output))
$appFolder=Join-Path $releaseRoot 'app'
if(-not $releaseRoot.StartsWith((Join-Path $projectRoot 'desktop/release'),[StringComparison]::OrdinalIgnoreCase)){throw 'Release output must stay within desktop/release'}
New-Item -ItemType Directory -Path $appFolder -Force | Out-Null
dotnet publish (Join-Path $projectRoot 'desktop/FullAIon2Companion/FullAIon2Companion.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -o $appFolder -v quiet
if($LASTEXITCODE -ne 0){throw 'Publish failed'}
python (Join-Path $PSScriptRoot 'optimize-companion-icons.py') $appFolder
if($LASTEXITCODE -ne 0){throw 'Icon optimization failed (requires Pillow)'}
$compiler=Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'
if(-not(Test-Path -LiteralPath $compiler)){throw 'Inno Setup 6 compiler missing'}
& $compiler "/DAppVersion=$Version" "/DSourceDir=$appFolder" "/DOutputDir=$releaseRoot" (Join-Path $projectRoot 'desktop/installer/FullAIon2Companion.iss') /Qp
if($LASTEXITCODE -ne 0){throw 'Installer build failed'}
$installer=Get-Item -LiteralPath (Join-Path $releaseRoot "FullAIon2Companion-$Version-setup.exe")
if($installer.Length -gt 100MB){throw 'Installer exceeds public download limit'}
$hash=(Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
@{version=$Version;bytes=$installer.Length;sha256=$hash;file=$installer.Name;architecture='windows-x64';selfContained=$true} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $releaseRoot 'release.json') -Encoding utf8
Write-Output "Installer ready: $($installer.FullName) ($([Math]::Round($installer.Length/1MB,1)) MB) SHA256=$hash"

