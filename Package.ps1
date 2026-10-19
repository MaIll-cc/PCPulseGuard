$ErrorActionPreference='Stop'
$match=[regex]::Match([IO.File]::ReadAllText((Join-Path $PSScriptRoot 'AppInfo.cs')),'Version="([0-9.]+)"')
if(!$match.Success){throw 'Could not determine application version.'};$version=$match.Groups[1].Value
$release=Join-Path $PSScriptRoot 'release';New-Item -ItemType Directory -Path $release -Force|Out-Null
$stage=Join-Path $release ('stage-'+[guid]::NewGuid().ToString('N'));$portable=Join-Path $stage 'portable';$source=Join-Path $stage 'source'
New-Item -ItemType Directory -Path $portable,$source,(Join-Path $source 'assets')|Out-Null
$docs=@('README.txt','README.md','README.zh-CN.md','LICENSE','CHANGELOG.md','SECURITY.md','CONTRIBUTING.md')
foreach($file in @('PCPulseGuard.Core.exe','PCPulseGuard.exe','PulseChecks.exe')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('bin\'+$file)) -Destination (Join-Path $portable $file)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\PCPulseGuard.ico') -Destination (Join-Path $portable 'PCPulseGuard.ico')
foreach($file in $docs+@('Install.ps1','Uninstall.ps1','SetStartup.ps1')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $portable $file)}
[IO.File]::WriteAllText((Join-Path $portable 'version.txt'),$version,[Text.UTF8Encoding]::new($false))
$sources=@('AppInfo.cs','PulseShared.cs','InputBridge.cs','InputMonitor.cs','LoadMonitor.cs','GpuPulse.cs','PulseEngine.cs','Tray.cs','Localization.cs','StartupManager.cs','SetStartup.ps1','Languages.tsv','UpdateChecker.cs','Checks.cs','Build.ps1','IntegrationChecks.ps1','Install.ps1','Uninstall.ps1','Package.ps1','.gitignore','.gitattributes','.editorconfig','RELEASE_NOTES.md')+$docs
foreach($file in $sources){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $source $file)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\PCPulseGuard.ico') -Destination (Join-Path $source 'assets\PCPulseGuard.ico')
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot '.github')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot '.github') -Destination (Join-Path $source '.github') -Recurse}
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'docs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $source 'docs') -Recurse;Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination (Join-Path $portable 'docs') -Recurse}
$portableZip=Join-Path $release ('PCPulseGuard-'+$version+'-windows-x64.zip');$sourceZip=Join-Path $release ('PCPulseGuard-'+$version+'-source.zip')
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $portableZip -Force
Compress-Archive -Path (Join-Path $source '*') -DestinationPath $sourceZip -Force
$hashes=foreach($path in @($portableZip,$sourceZip)){$hash=Get-FileHash -LiteralPath $path -Algorithm SHA256;$hash.Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($path)}
[IO.File]::WriteAllLines((Join-Path $release 'SHA256SUMS.txt'),$hashes,[Text.UTF8Encoding]::new($false))
Get-Item -LiteralPath $portableZip,$sourceZip,(Join-Path $release 'SHA256SUMS.txt')|Select-Object FullName,Length
