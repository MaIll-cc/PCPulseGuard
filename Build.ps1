$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$bin=Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $bin -Force|Out-Null
$common=@('/nologo','/platform:x64','/codepage:65001','/optimize+','/reference:System.Web.Extensions.dll','/reference:System.Windows.Forms.dll','/reference:System.Drawing.dll','/reference:Microsoft.CSharp.dll')
$shared=Join-Path $PSScriptRoot 'PulseShared.cs'
$info=Join-Path $PSScriptRoot 'AppInfo.cs'
$ui=@('Localization.cs','UpdateChecker.cs','StartupManager.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
$resource='/resource:'+(Join-Path $PSScriptRoot 'Languages.tsv')+',Languages.tsv'
$sources=@('InputBridge.cs','LoadMonitor.cs','GpuPulse.cs','PulseEngine.cs')|ForEach-Object {Join-Path $PSScriptRoot $_}
& $compiler @common /target:winexe ('/out:'+(Join-Path $bin 'PCPulseGuard.Core.exe')) $shared $info @sources
if($LASTEXITCODE -ne 0){throw 'Backend compilation failed'}
& $compiler @common /target:winexe ('/win32icon:'+(Join-Path $PSScriptRoot 'assets\PCPulseGuard.ico')) ('/out:'+(Join-Path $bin 'PCPulseGuard.exe')) $resource $shared $info @ui (Join-Path $PSScriptRoot 'Tray.cs')
if($LASTEXITCODE -ne 0){throw 'UI compilation failed'}
& $compiler @common /target:exe ('/out:'+(Join-Path $bin 'PulseChecks.exe')) $resource $shared $info @ui (Join-Path $PSScriptRoot 'LoadMonitor.cs') (Join-Path $PSScriptRoot 'GpuPulse.cs') (Join-Path $PSScriptRoot 'Checks.cs')
if($LASTEXITCODE -ne 0){throw 'Verification build failed'}
& (Join-Path $bin 'PulseChecks.exe')
if($LASTEXITCODE -ne 0){throw 'Verification failed'}
