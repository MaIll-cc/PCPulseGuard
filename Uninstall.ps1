param([string]$InstallRoot=(Join-Path $env:ProgramData 'PCPulseGuard'))
$ErrorActionPreference='Stop'
if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run Uninstall.ps1 as administrator.'}
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
if($InstallRoot -notlike ($env:ProgramData+'\*')){throw 'Invalid installation directory.'}
if(((Get-Item -LiteralPath $InstallRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Refusing reparse point.'}
$marker=Get-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($marker.product -ne 'PCPulseGuard' -or $marker.taskPrefix -notmatch '^[A-Za-z0-9-]{1,48}$'){throw 'Not a recognized installation.'}
$paths=@('PCPulseGuard.exe','PCPulseGuard.Core.exe')|ForEach-Object {Join-Path $InstallRoot $_}
$processes=Get-CimInstance Win32_Process|Where-Object {$paths -contains $_.ExecutablePath}
foreach($suffix in @('Watchdog','Activity','Tray')){$name=$marker.taskPrefix+'-'+$suffix;Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue}
$processes|ForEach-Object {Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue;Wait-Process -Id $_.ProcessId -Timeout 10 -ErrorAction SilentlyContinue}
function Remove-Retry([string]$Path){for($n=0;$n -lt 20;$n++){try{Remove-Item -LiteralPath $Path -Force;return}catch{if($n -eq 19){throw};Start-Sleep -Milliseconds 500}}}
$userProfileRecord=Get-CimInstance Win32_UserProfile|Where-Object {$_.SID -eq $marker.userSid}|Select-Object -First 1
if($userProfileRecord){$shell=New-Object -ComObject WScript.Shell;$folders=@((Join-Path $userProfileRecord.LocalPath 'Desktop'),(Join-Path $userProfileRecord.LocalPath 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs'));if($marker.userSid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){$folders=@([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))};foreach($folder in $folders){$path=Join-Path $folder 'PCPulseGuard.lnk';if(Test-Path -LiteralPath $path){if($paths -contains $shell.CreateShortcut($path).TargetPath){Remove-Item -LiteralPath $path}}}}
foreach($file in @('PCPulseGuard.exe','PCPulseGuard.Core.exe','PCPulseGuard.ico','README.txt','README.md','README.zh-CN.md','LICENSE','Uninstall.ps1','SetStartup.ps1','version.txt','installation.json')){$path=Join-Path $InstallRoot $file;if(Test-Path -LiteralPath $path){Remove-Retry $path}}
Write-Output 'Uninstalled. Settings, logs and backups were retained in the installation directory.'
