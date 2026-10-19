param(
 [Parameter(Mandatory=$true)][string]$InstallRoot,
 [ValidateSet('Apply','Start')][string]$Mode='Apply',
 [ValidateSet('On','Off')][string]$Enabled='On',
 [ValidateSet('On','Off')][string]$Silent='On'
)
$ErrorActionPreference='Stop'
$backup=$null;$snapshots=@{};$changed=$false
try{
 if(!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Administrator authorization is required.'}
 $InstallRoot=[IO.Path]::GetFullPath($InstallRoot).TrimEnd('\')
 if($InstallRoot -notlike ($env:ProgramData+'\*') -or $InstallRoot -ne [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')){throw 'Run the protected script from its installation directory.'}
 if(((Get-Item -LiteralPath $InstallRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Refusing reparse point.'}
 $marker=Get-Content -LiteralPath (Join-Path $InstallRoot 'installation.json') -Raw -Encoding UTF8|ConvertFrom-Json
 if(!$marker.machine -or $marker.product -ne 'PCPulseGuard' -or $marker.taskPrefix -notmatch '^[A-Za-z0-9-]{1,48}$'){throw 'Unrecognized installation.'}
 $tasks=@{}
 foreach($suffix in @('Watchdog','Activity','Tray')){
  $name=$marker.taskPrefix+'-'+$suffix;$task=Get-ScheduledTask -TaskName $name
  $expected=Join-Path $InstallRoot $(if($suffix -eq 'Tray'){'PCPulseGuard.exe'}else{'PCPulseGuard.Core.exe'})
  if($task.Actions.Count -ne 1 -or $task.Actions[0].Execute -ne $expected){throw 'Unexpected task action.'}
  $tasks[$suffix]=$task;$snapshots[$name]=Export-ScheduledTask -TaskName $name
 }
 if($Mode -eq 'Apply'){
  $backup=Join-Path $InstallRoot ('backups\startup-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $backup|Out-Null
  foreach($name in $snapshots.Keys){$snapshots[$name]|Set-Content -LiteralPath (Join-Path $backup ($name+'.xml')) -Encoding Unicode}
  $changed=$true
  $argument=$(if($Silent -eq 'On'){'--tray'}else{'--show'})+' --root "'+$InstallRoot+'"'
  $action=New-ScheduledTaskAction -Execute (Join-Path $InstallRoot 'PCPulseGuard.exe') -Argument $argument -WorkingDirectory $InstallRoot
  Set-ScheduledTask -TaskName ($marker.taskPrefix+'-Tray') -Action $action|Out-Null
  foreach($suffix in $tasks.Keys){$name=$marker.taskPrefix+'-'+$suffix;if($Enabled -eq 'On'){Enable-ScheduledTask -TaskName $name|Out-Null}else{Disable-ScheduledTask -TaskName $name|Out-Null}}
 }
 # Start a manually opened installed copy even when future automatic startup is off.
 # Temporarily enabling a task to run it does not enable its next boot/logon trigger.
 if($Mode -eq 'Start' -or $Enabled -eq 'On'){
  foreach($suffix in @('Watchdog','Activity')){
   $name=$marker.taskPrefix+'-'+$suffix;$task=Get-ScheduledTask -TaskName $name;$wasEnabled=$task.Settings.Enabled
   try{if(!$wasEnabled){Enable-ScheduledTask -TaskName $name|Out-Null};Start-ScheduledTask -TaskName $name}finally{if(!$wasEnabled){Disable-ScheduledTask -TaskName $name|Out-Null}}
  }
 }
 exit 0
}catch{
 if($changed){foreach($name in $snapshots.Keys){try{Register-ScheduledTask -TaskName $name -Xml $snapshots[$name] -Force|Out-Null}catch{Write-Warning ('Could not restore '+$name)}}}
 Write-Error $_.Exception.Message -ErrorAction Continue
 try{[IO.File]::WriteAllText((Join-Path $InstallRoot 'activity\startup-last-error.txt'),$_.Exception.Message,[Text.UTF8Encoding]::new($false))}catch{}
 exit 1
}
