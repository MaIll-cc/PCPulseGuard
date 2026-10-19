param(
 [string]$InstallRoot=(Join-Path $env:ProgramData 'PCPulseGuard'),
 [string]$TaskPrefix='PCPulseGuard',
 [string]$UserSid=([Security.Principal.WindowsIdentity]::GetCurrent().User.Value),
 [string]$UserProfilePath=$env:USERPROFILE,
 [switch]$VerifyGpu,
 [switch]$NoShortcuts
)
$ErrorActionPreference='Stop'
$product='PCPulseGuard'
$coreName='PCPulseGuard.Core.exe';$trayName='PCPulseGuard.exe'
$report=Join-Path $PSScriptRoot 'install-result.json'
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run Install.ps1 as administrator.'}
$InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
if($InstallRoot -notlike ($env:ProgramData+'\*')){throw 'Use a dedicated directory under ProgramData.'}
if($TaskPrefix -notmatch '^[A-Za-z0-9-]{1,48}$'){throw 'Invalid task prefix.'}
$sid=[Security.Principal.SecurityIdentifier]::new($UserSid)
$sourceRoot=if(Test-Path -LiteralPath (Join-Path $PSScriptRoot ('bin\'+$trayName))){Join-Path $PSScriptRoot 'bin'}else{$PSScriptRoot}
$iconSource=if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'assets\PCPulseGuard.ico')){Join-Path $PSScriptRoot 'assets\PCPulseGuard.ico'}else{Join-Path $sourceRoot 'PCPulseGuard.ico'}
$versionInfo=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $sourceRoot $trayName))
$version=([version]$versionInfo.FileVersion).ToString(3)
$names=@("$TaskPrefix-Watchdog","$TaskPrefix-Activity","$TaskPrefix-Tray")
$documents=@('README.txt','README.md','README.zh-CN.md','LICENSE','Uninstall.ps1','SetStartup.ps1','version.txt')
$deployed=@($coreName,$trayName,'PCPulseGuard.ico','installation.json')+$documents
$legacy=@('KeepWorkstationAlive.exe','KeepWorkstationAlive.Core.exe','KeepWorkstationAlive.ico','WorkstationWatchdog.exe','WorkstationTray.exe','WorkstationPulse.ico')
$managed=$deployed+$legacy
$backup=Join-Path $InstallRoot ('backups\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,8))
$oldTasks=@{};$oldStartup=@{};$startHidden=$true;$oldFiles=@{};$shortcuts=[Collections.Generic.List[object]]::new();$changed=$false;$checkTask=$null;$gpuReport=$null
function Reject-Link([string]$Path){if(Test-Path -LiteralPath $Path){if(((Get-Item -LiteralPath $Path -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw ('Refusing reparse point: '+$Path)}}}
function Copy-Retry([string]$Source,[string]$Destination){for($n=0;$n -lt 20;$n++){try{Copy-Item -LiteralPath $Source -Destination $Destination -Force;return}catch{if($n -eq 19){throw};Start-Sleep -Milliseconds 500}}}
function Remove-Retry([string]$Path){for($n=0;$n -lt 20;$n++){try{Remove-Item -LiteralPath $Path -Force;return}catch{if($n -eq 19){throw};Start-Sleep -Milliseconds 500}}}
function Grant-TaskReadExecute([string]$Name){
 $service=New-Object -ComObject Schedule.Service;$service.Connect();$folder=$service.GetFolder('\');$task=$folder.GetTask($Name)
 try{
  $descriptor=[Security.AccessControl.RawSecurityDescriptor]::new($task.GetSecurityDescriptor(4));$mask=[int][Security.AccessControl.FileSystemRights]::ReadAndExecute
  $found=@($descriptor.DiscretionaryAcl|Where-Object {$_ -is [Security.AccessControl.CommonAce] -and $_.SecurityIdentifier -eq $sid -and $_.AceQualifier -eq [Security.AccessControl.AceQualifier]::AccessAllowed -and ($_.AccessMask -band $mask) -eq $mask}).Count -gt 0
  if(!$found){$ace=[Security.AccessControl.CommonAce]::new([Security.AccessControl.AceFlags]::None,[Security.AccessControl.AceQualifier]::AccessAllowed,$mask,$sid,$false,$null);$descriptor.DiscretionaryAcl.InsertAce($descriptor.DiscretionaryAcl.Count,$ace);$task.SetSecurityDescriptor($descriptor.GetSddlForm([Security.AccessControl.AccessControlSections]::Access),0)}
 }finally{foreach($item in @($task,$folder,$service)){[Runtime.InteropServices.Marshal]::FinalReleaseComObject($item)|Out-Null}}
}
function Stop-App{
 $paths=@($coreName,$trayName,'KeepWorkstationAlive.exe','KeepWorkstationAlive.Core.exe','WorkstationWatchdog.exe','WorkstationTray.exe')|ForEach-Object {Join-Path $InstallRoot $_}
 $processes=Get-CimInstance Win32_Process|Where-Object {$paths -contains $_.ExecutablePath}
 foreach($name in $names){Stop-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue}
 foreach($proc in $processes){Stop-Process -Id $proc.ProcessId -Force -ErrorAction SilentlyContinue;Wait-Process -Id $proc.ProcessId -Timeout 10 -ErrorAction SilentlyContinue}
}
function Protect-File([string]$Path){
 if(!(Test-Path -LiteralPath $Path)){return};Reject-Link $Path
 $acl=New-Object Security.AccessControl.FileSecurity;$acl.SetAccessRuleProtection($true,$false);$acl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')){$acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(([Security.Principal.SecurityIdentifier]::new($identity)),'FullControl','Allow')))}
 $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'ReadAndExecute','Allow')));Set-Acl -LiteralPath $Path -AclObject $acl
}
try{
 foreach($file in @($coreName,$trayName)){if(!(Test-Path -LiteralPath (Join-Path $sourceRoot $file))){throw ('Build or extract the application first: '+$file)}}
 Reject-Link $InstallRoot
 foreach($child in @('activity','backups','installation.json')+$managed){Reject-Link (Join-Path $InstallRoot $child)}
 if((Test-Path -LiteralPath $InstallRoot) -and @(Get-ChildItem -LiteralPath $InstallRoot -Force).Count -gt 0){
  try{$marker=Get-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Raw -Encoding UTF8|ConvertFrom-Json}catch{$marker=$null}
  $isLegacy=$marker -and $marker.machine -and ((Test-Path -LiteralPath (Join-Path $InstallRoot 'WorkstationTray.exe')) -or ($marker.product -eq 'KeepWorkstationAlive' -and (Test-Path -LiteralPath (Join-Path $InstallRoot 'KeepWorkstationAlive.exe'))))
  if(!$isLegacy -and (!$marker -or $marker.product -ne $product)){throw 'This nonempty directory does not contain a recognized installation.'}
 }
 New-Item -ItemType Directory -Path $InstallRoot -Force|Out-Null
 $rootAcl=New-Object Security.AccessControl.DirectorySecurity;$rootAcl.SetAccessRuleProtection($true,$false);$rootAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'))
 foreach($identity in @('S-1-5-18','S-1-5-32-544')){$rootAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule(([Security.Principal.SecurityIdentifier]::new($identity)),'FullControl','ContainerInherit,ObjectInherit','None','Allow')))}
 $rootAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')));Set-Acl -LiteralPath $InstallRoot -AclObject $rootAcl
 foreach($file in $managed){Protect-File (Join-Path $InstallRoot $file)}
 New-Item -ItemType Directory -Path $backup -Force|Out-Null
 foreach($name in $names){try{$xml=Export-ScheduledTask -TaskName $name;$oldTasks[$name]=$xml;$priorTask=Get-ScheduledTask -TaskName $name;$oldStartup[$name]=$priorTask.Settings.Enabled;if($name -eq $names[2]){$startHidden=$priorTask.Actions[0].Arguments.StartsWith('--tray')};$xml|Set-Content -LiteralPath (Join-Path $backup ($name+'.xml')) -Encoding Unicode}catch{if(Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue){throw}}}
 foreach($file in $managed){$path=Join-Path $InstallRoot $file;if(Test-Path -LiteralPath $path){Copy-Item -LiteralPath $path -Destination (Join-Path $backup $file) -Force;$oldFiles[$file]=$true}}
 $prefs=Join-Path $InstallRoot 'activity\preferences.json';Reject-Link $prefs;if(Test-Path -LiteralPath $prefs){Copy-Item -LiteralPath $prefs -Destination (Join-Path $backup 'preferences.json')}
 $changed=$true;Stop-App;$startupAfter=[datetime]::UtcNow
 $activity=Join-Path $InstallRoot 'activity';New-Item -ItemType Directory -Path $activity -Force|Out-Null
 $activityAcl=New-Object Security.AccessControl.DirectorySecurity;$activityAcl.SetAccessRuleProtection($false,$false);$activityAcl.SetOwner([Security.Principal.SecurityIdentifier]::new('S-1-5-32-544'));$activityAcl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid,'Modify','ContainerInherit,ObjectInherit','None','Allow')));Set-Acl -LiteralPath $activity -AclObject $activityAcl
 foreach($file in @($coreName,$trayName)){Copy-Retry (Join-Path $sourceRoot $file) (Join-Path $InstallRoot $file)}
 Copy-Retry $iconSource (Join-Path $InstallRoot 'PCPulseGuard.ico')
 foreach($file in $documents){if(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file)){Copy-Retry (Join-Path $PSScriptRoot $file) (Join-Path $InstallRoot $file)}}
 [IO.File]::WriteAllText((Join-Path $InstallRoot 'version.txt'),$version,[Text.UTF8Encoding]::new($false))
 @{product=$product;machine=$true;version=$version;installedAt=[datetimeoffset]::Now.ToString('o');taskPrefix=$TaskPrefix;userSid=$UserSid}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Encoding UTF8
 foreach($file in $deployed){Protect-File (Join-Path $InstallRoot $file)}
 $settings=New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([timespan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
 $system=New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
 $interactive=New-ScheduledTaskPrincipal -UserId $UserSid -LogonType Interactive -RunLevel Limited
 $rootArg=' --root "'+$InstallRoot+'"'
 $core=New-ScheduledTask -Action (New-ScheduledTaskAction -Execute (Join-Path $InstallRoot $coreName) -Argument $rootArg -WorkingDirectory $InstallRoot) -Trigger (New-ScheduledTaskTrigger -AtStartup) -Principal $system -Settings $settings
 Register-ScheduledTask -TaskName $names[0] -InputObject $core -Force|Out-Null
 $inputTask=New-ScheduledTask -Action (New-ScheduledTaskAction -Execute (Join-Path $InstallRoot $coreName) -Argument ('--activity'+$rootArg) -WorkingDirectory $InstallRoot) -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $UserSid) -Principal $interactive -Settings $settings
 Register-ScheduledTask -TaskName $names[1] -InputObject $inputTask -Force|Out-Null
 $uiArgument=$(if($startHidden){'--tray'}else{'--show'})+$rootArg
 $ui=New-ScheduledTask -Action (New-ScheduledTaskAction -Execute (Join-Path $InstallRoot $trayName) -Argument $uiArgument -WorkingDirectory $InstallRoot) -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $UserSid) -Principal $interactive -Settings $settings
 Register-ScheduledTask -TaskName $names[2] -InputObject $ui -Force|Out-Null
 foreach($name in $names){Grant-TaskReadExecute $name}
 $shell=New-Object -ComObject WScript.Shell
 $folders=@((Join-Path $UserProfilePath 'Desktop'),(Join-Path $UserProfilePath 'AppData\Roaming\Microsoft\Windows\Start Menu\Programs'))
 if($UserSid -eq [Security.Principal.WindowsIdentity]::GetCurrent().User.Value){$folders=@([Environment]::GetFolderPath('DesktopDirectory'),[Environment]::GetFolderPath('Programs'))}
 if($NoShortcuts){$folders=@()}
 foreach($folder in $folders){if(!(Test-Path -LiteralPath $folder)){continue};$path=Join-Path $folder ($product+'.lnk');$record=@{Path=$path;Existed=(Test-Path -LiteralPath $path);Backup=(Join-Path $backup ('shortcut-'+$shortcuts.Count+'.lnk'))};if($record.Existed){$existing=$shell.CreateShortcut($path);if($existing.TargetPath -ne (Join-Path $InstallRoot $trayName)){throw 'A shortcut with the same name belongs to another installation.'};Copy-Item -LiteralPath $path -Destination $record.Backup};$shortcuts.Add($record);$shortcut=$shell.CreateShortcut($path);$shortcut.TargetPath=Join-Path $InstallRoot $trayName;$shortcut.Arguments='--show'+$rootArg;$shortcut.WorkingDirectory=$InstallRoot;$shortcut.IconLocation=Join-Path $InstallRoot 'PCPulseGuard.ico';$shortcut.Description='PCPulseGuard settings and status';$shortcut.Save()}
 foreach($name in $names){Start-ScheduledTask -TaskName $name;if($oldStartup.ContainsKey($name) -and !$oldStartup[$name]){Disable-ScheduledTask -TaskName $name|Out-Null}}
 $deadline=[datetime]::UtcNow.AddSeconds(15)
 do{Start-Sleep -Milliseconds 500;try{$state=Get-Content -LiteralPath (Join-Path $InstallRoot 'pulse-status.json') -Raw -Encoding UTF8|ConvertFrom-Json}catch{$state=$null};$healthy=$state -and $state.version -eq $version -and ([datetime]$state.time).ToUniversalTime() -gt $startupAfter -and (Get-Process -Id $state.pid -ErrorAction SilentlyContinue).ProcessName -eq [IO.Path]::GetFileNameWithoutExtension($coreName) -and ([datetime]::UtcNow-([datetime]$state.time).ToUniversalTime()).TotalSeconds -lt 5}while(!$healthy -and [datetime]::UtcNow -lt $deadline)
 if(!$healthy){throw 'New background engine did not report healthy startup.'}
 if($VerifyGpu){$checkFolder=Join-Path $backup 'gpu-verification';New-Item -ItemType Directory -Path $checkFolder -Force|Out-Null;Copy-Retry (Join-Path $sourceRoot 'PulseChecks.exe') (Join-Path $checkFolder 'PulseChecks.exe');$checkTask=$TaskPrefix+'-Check-'+[guid]::NewGuid().ToString('N').Substring(0,8);$check=New-ScheduledTask -Action (New-ScheduledTaskAction -Execute (Join-Path $checkFolder 'PulseChecks.exe') -Argument '--gpu-check' -WorkingDirectory $checkFolder) -Principal $system -Settings (New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Minutes 1));Register-ScheduledTask -TaskName $checkTask -InputObject $check|Out-Null;Start-ScheduledTask -TaskName $checkTask;$gpuReport=Join-Path $checkFolder 'checks-gpu.json';$deadline=[datetime]::UtcNow.AddSeconds(30);do{Start-Sleep -Milliseconds 500}while(!(Test-Path -LiteralPath $gpuReport) -and [datetime]::UtcNow -lt $deadline);if(!(Test-Path -LiteralPath $gpuReport)){throw 'GPU verification in the boot/SYSTEM session did not complete.'};$gpuCheck=Get-Content -LiteralPath $gpuReport -Raw -Encoding UTF8|ConvertFrom-Json;if(@($gpuCheck.results|Where-Object {$_.name -eq 'Direct3D GPU pulse and result verification' -and $_.passed}).Count -ne 1){throw 'GPU verification report did not confirm successful computation.'}}
 # Old executables are already backed up; remove legacy launchers only after successful startup.
 foreach($file in $legacy){$path=Join-Path $InstallRoot $file;if(Test-Path -LiteralPath $path){Remove-Retry $path}}
 foreach($folder in $folders){foreach($shortcutName in @('工作站心跳.lnk','KeepWorkstationAlive.lnk')){$path=Join-Path $folder $shortcutName;if(Test-Path -LiteralPath $path){$old=$shell.CreateShortcut($path);if(@((Join-Path $InstallRoot 'WorkstationTray.exe'),(Join-Path $InstallRoot 'KeepWorkstationAlive.exe')) -contains $old.TargetPath){Copy-Item -LiteralPath $path -Destination (Join-Path $backup ('legacy-shortcut-'+[guid]::NewGuid().ToString('N')+'.lnk'));Remove-Item -LiteralPath $path}}}}
 @{success=$true;version=$version;installRoot=$InstallRoot;backup=$backup;gpuVerificationReport=$gpuReport;tasks=@(Get-ScheduledTask -TaskName $names|Select-Object TaskName,State)}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $report -Encoding UTF8
}catch{
 $failure=$_.Exception.Message;$recovery='No running application was replaced.'
 if($changed){try{Stop-App;foreach($file in $managed){$path=Join-Path $InstallRoot $file;if($oldFiles.ContainsKey($file)){Copy-Retry (Join-Path $backup $file) $path}elseif(Test-Path -LiteralPath $path){Remove-Retry $path}};if(Test-Path -LiteralPath (Join-Path $backup 'preferences.json')){Copy-Retry (Join-Path $backup 'preferences.json') (Join-Path $InstallRoot 'activity\preferences.json')};foreach($record in $shortcuts){if($record.Existed){Copy-Retry $record.Backup $record.Path}elseif(Test-Path -LiteralPath $record.Path){Remove-Item -LiteralPath $record.Path}};foreach($name in $names){if($oldTasks.ContainsKey($name)){Register-ScheduledTask -TaskName $name -Xml $oldTasks[$name] -Force|Out-Null;Grant-TaskReadExecute $name;if($oldStartup.ContainsKey($name) -and !$oldStartup[$name]){Enable-ScheduledTask -TaskName $name|Out-Null};Start-ScheduledTask -TaskName $name;if($oldStartup.ContainsKey($name) -and !$oldStartup[$name]){Disable-ScheduledTask -TaskName $name|Out-Null}}else{Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction SilentlyContinue}};$recovery='Previous executables, settings, tasks and shortcuts restored.'}catch{$recovery='Recovery needs attention: '+$_.Exception.Message}}
 @{success=$false;error=$failure;recovery=$recovery;backup=$backup}|ConvertTo-Json|Set-Content -LiteralPath $report -Encoding UTF8;exit 1
}finally{if($checkTask){Stop-ScheduledTask -TaskName $checkTask -ErrorAction SilentlyContinue;Unregister-ScheduledTask -TaskName $checkTask -Confirm:$false -ErrorAction SilentlyContinue}}
