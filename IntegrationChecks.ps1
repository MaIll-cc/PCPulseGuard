$ErrorActionPreference='Stop'
$root=Join-Path $PSScriptRoot ('test-data\integration-'+(Get-Date -Format yyyyMMdd-HHmmss))
New-Item -ItemType Directory -Path (Join-Path $root 'activity') -Force|Out-Null
$exe=Join-Path $PSScriptRoot 'bin\PCPulseGuard.Core.exe'
$results=[System.Collections.Generic.List[object]]::new()
$settings=[ordered]@{Version=2;Enabled=$false;MouseEnabled=$true;MousePixels=1;CpuEnabled=$false;GpuEnabled=$false;IdleValue=2;IdleUnit='s';RepeatValue=3;RepeatUnit='s';DurationValue=1;DurationUnit='s';CpuPercent=5;GpuPercent=5;SkipHighLoad=$true;CpuBusyPercent=100;GpuBusyPercent=100;KeepSystemAwake=$true;KeepDisplayAwake=$false;MemoryEnabled=$false;DiskEnabled=$false}
function Read-Record([string]$name){try{Get-Content -LiteralPath (Join-Path $root $name) -Raw|ConvertFrom-Json}catch{$null}}
function Save-Settings{ $path=Join-Path $root 'activity\preferences.json';$tmp=$path+'.tmp';[IO.File]::WriteAllText($tmp,($settings|ConvertTo-Json -Compress),[Text.UTF8Encoding]::new($false));if(Test-Path -LiteralPath $path){[IO.File]::Replace($tmp,$path,$path+'.bak')}else{[IO.File]::Move($tmp,$path)} }
function Assert([string]$name,[bool]$passed){if(!$passed){throw ('Check failed: '+$name)};$results.Add([pscustomobject]@{name=$name;passed=$true})}
function Wait-Record([string]$name,[scriptblock]$condition,[int]$seconds=35){$deadline=[datetime]::UtcNow.AddSeconds($seconds);do{$record=Read-Record $name;if($record -and (& $condition $record)){return $record};Start-Sleep -Milliseconds 250}while([datetime]::UtcNow -lt $deadline);throw ('Timed out: '+$name+' '+(& $condition).ToString())}
$core=$null;$bridge=$null
try{
 Save-Settings
 $bridge=Start-Process -FilePath $exe -ArgumentList ('--activity --root "'+$root+'"') -WindowStyle Hidden -PassThru
 $core=Start-Process -FilePath $exe -ArgumentList ('--root "'+$root+'"') -WindowStyle Hidden -PassThru
 $session=(Get-Process -Id $PID).SessionId;$activity='activity\activity-'+$session+'.json'
 $a=Wait-Record $activity {param($v)$v.metadataHooks -and $v.mouseAvailable}
 $disabled=Wait-Record 'pulse-status.json' {param($v)$v.code -eq 'disabled'}
 Assert 'master off is observed by scheduler' ($disabled.code -eq 'disabled')
 $settings.Enabled=$true;Save-Settings
 $first=Wait-Record 'pulse-last.json' {param($v)$v.status -eq 'completed' -and $v.mouse -eq 'moved'}
 $a=Read-Record $activity
 Assert 'default one-pixel mouse action acknowledged' ($a.mouseAckId -eq $first.id -and $a.mouseAckPixels -eq 1)
 $before=$a.inputUtc
 $cooldown=Wait-Record 'pulse-status.json' {param($v)$v.code -eq 'cooldown'}
 Assert 'repeat countdown after heartbeat' ($cooldown.remainingSeconds -gt 0)
 $settings.MousePixels=37;Save-Settings
 $second=Wait-Record 'pulse-last.json' {param($v)$v.id -ne $first.id -and $v.status -eq 'completed' -and $v.mouse -eq 'moved'}
 $a=Read-Record $activity
 Assert 'custom 37-pixel mouse action acknowledged' ($a.mouseAckId -eq $second.id -and $a.mouseAckPixels -eq 37)
 Assert 'own mouse heartbeat does not reset human idle' ($a.inputUtc -eq $before)
 $settings.MouseEnabled=$false;$settings.CpuEnabled=$true;$settings.GpuEnabled=$true;$settings.DurationValue=3;Save-Settings
 $combined=Wait-Record 'pulse-last.json' {param($v)$v.id -ne $second.id -and $v.status -eq 'completed' -and $v.gpu -eq 'completed'}
 Assert 'CPU and GPU pulse completes together' ($combined.measuredCpuPercent -gt 0 -and $combined.gpuBatches -gt 0)
 $settings.GpuEnabled=$false;$settings.DurationValue=30;Save-Settings
 $running=Wait-Record 'pulse-status.json' {param($v)$v.running -and $v.settings.DurationValue -eq 30}
 $progress=Wait-Record 'pulse-progress.json' {param($v)$v.id -eq $running.jobId -and $v.elapsedSeconds -ge 1}
 $settings.Enabled=$false;try{Save-Settings}catch{}
 $cancelled=Wait-Record 'pulse-last.json' {param($v)$v.id -eq $running.jobId -and $v.status -eq 'cancelled'} 8
 $off=Wait-Record 'pulse-status.json' {param($v)$v.code -eq 'disabled'} 8
 Assert 'master off stops an active pulse promptly' ($cancelled.status -eq 'cancelled' -and !$off.running)
 Assert 'release this app power request when disabled' (!$off.settings.Enabled)
 $settings.Enabled=$true;Save-Settings
 $orphan=Wait-Record 'pulse-status.json' {param($v)$v.running -and $v.settings.DurationValue -eq 30}
 $orphanProgress=Wait-Record 'pulse-progress.json' {param($v)$v.id -eq $orphan.jobId -and $v.elapsedSeconds -ge 1}
 Stop-Process -Id $core.Id -Force;Wait-Process -Id $core.Id -Timeout 5 -ErrorAction SilentlyContinue
 $orphanEnd=Wait-Record 'pulse-last.json' {param($v)$v.id -eq $orphan.jobId -and $v.status -eq 'cancelled' -and $v.reason -eq 'Parent engine stopped'} 8
 Wait-Process -Id $orphan.workerPid -Timeout 5 -ErrorAction SilentlyContinue
 Assert 'orphaned load worker stops after engine exits' (!(Get-Process -Id $orphan.workerPid -ErrorAction SilentlyContinue))
 $report=[ordered]@{time=[datetimeoffset]::Now.ToString('o');passed=$results.Count;results=$results;mouseOne=$first;mouseCustom=$second;combined=$combined;cancelled=$cancelled;orphanCancelled=$orphanEnd;dataRoot=$root}
 $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'bin\checks-integration.json') -Encoding UTF8
 [pscustomobject]@{passed=$results.Count;report=(Join-Path $PSScriptRoot 'bin\checks-integration.json')}
}finally{
 $settings.Enabled=$false;Save-Settings
 foreach($p in @($core,$bridge)){if($p){Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue;Wait-Process -Id $p.Id -Timeout 5 -ErrorAction SilentlyContinue}}
}
