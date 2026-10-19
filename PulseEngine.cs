using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

public sealed class PulseJob {
    public string Id,InputUtc,Revision,ParentStartedUtc;public int ParentPid;public PulseSettings Settings;
}

public static class PulseWorker {
    [DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
    [DllImport("kernel32.dll")]static extern UIntPtr SetThreadAffinityMask(IntPtr thread,UIntPtr mask);
    static double sink;
    static bool ParentAlive(PulseJob job){try{using(var parent=Process.GetProcessById(job.ParentPid))return !parent.HasExited&&parent.ProcessName==Path.GetFileNameWithoutExtension(AppInfo.CoreFile)&&parent.StartTime.ToUniversalTime().ToString("o")==job.ParentStartedUtc;}catch{return false;}}
    public static int Run(string root,string id) {
        Guid guid;if(!Guid.TryParseExact(id,"N",out guid))return 1;
        PulseJob job;
        try {job=PulseIO.Json.Deserialize<PulseJob>(PulseIO.ReadText(Path.Combine(root,"pulse-job-"+id+".json"),16384));}catch{return 1;}
        var s=job.Settings;s.Normalize();var watch=Stopwatch.StartNew();var stop=new ManualResetEvent(false);var threads=new List<Thread>();
        string reason="",gpuResult=s.GpuEnabled?"pending":"disabled",mouseResult=s.MouseEnabled?"pending":"disabled",memoryResult="disabled",diskResult="disabled";
        string gpuError="";long batches=0;bool cancelled=false;DateTime baseline=DateTime.Parse(job.InputUtc).ToUniversalTime();
        double cpuStart=Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds;
        try {
            if(!s.Enabled||s.Revision!=PulseSettings.Load(root).Revision)throw new OperationCanceledException("Settings changed");
            if(!ParentAlive(job))throw new OperationCanceledException("Parent engine stopped");
            var initial=InputBridge.Snapshot(root);if(!initial.Known||initial.LastUtc>baseline||initial.IdleSeconds<s.IdleSeconds)throw new OperationCanceledException("User activity");
            Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.BelowNormal;
            if(s.MemoryEnabled){byte[] m=new byte[64*1024*1024];for(int i=0;i<m.Length;i+=4096)m[i]=(byte)(i/4096);for(int i=0;i<m.Length;i+=4096)if(m[i]!=(byte)(i/4096))throw new Exception("Memory verification mismatch");GC.KeepAlive(m);memoryResult="64 MiB touched and verified";}
            if(s.DiskEnabled){string path=Path.Combine(root,"pulse-"+id+".tmp");using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None,65536,FileOptions.DeleteOnClose|FileOptions.WriteThrough)){byte[] b=new byte[1024*1024];for(int i=0;i<4;i++)f.Write(b,0,b.Length);f.Flush(true);}diskResult="4 MiB temporary write completed";}
            if(s.MouseEnabled) {
                mouseResult=initial.MouseSessions.Count>0?"pending":"no_unlocked_desktop";
                foreach(int session in initial.MouseSessions)PulseIO.Save(Path.Combine(root,"activity","mouse-request-"+session+".json"),new {id=id,revision=s.Revision,expiresUtc=DateTime.UtcNow.AddSeconds(5).ToString("o")});
            }
            if(s.CpuEnabled)for(int index=0;index<Environment.ProcessorCount;index++) {
                int core=index;var t=new Thread(delegate(){
                    try {
                        if(core<64)SetThreadAffinityMask(GetCurrentThread(),new UIntPtr(1UL<<core));
                        stop.WaitOne(core*100/Math.Max(1,Environment.ProcessorCount));var cycle=new Stopwatch();double x=1;
                        while(!stop.WaitOne(0)){cycle.Restart();while(cycle.Elapsed.TotalMilliseconds<s.CpuPercent&&!stop.WaitOne(0)){for(int n=0;n<128;n++)x=Math.Sqrt(x+1.001);}sink=x;int rest=(int)Math.Max(0,100-cycle.Elapsed.TotalMilliseconds);if(rest>0)stop.WaitOne(rest);}
                    }catch(Exception e){gpuError="CPU worker: "+e.Message;stop.Set();}
                });t.IsBackground=true;t.Priority=ThreadPriority.Lowest;threads.Add(t);t.Start();
            }
            if(s.GpuEnabled) {
                var t=new Thread(delegate(){try{using(var gpu=new GpuPulse()){gpuResult="running";batches=gpu.Run(s.GpuPercent,stop);gpuResult="completed";}}catch(Exception e){gpuError=e.GetType().Name+": "+e.Message;gpuResult="unavailable";}});t.IsBackground=true;t.Priority=ThreadPriority.BelowNormal;threads.Add(t);t.Start();
            }
            DateTime nextCheck=DateTime.MinValue;
            while(watch.Elapsed.TotalSeconds<s.DurationSeconds) {
                if(stop.WaitOne(100))break;
                if(DateTime.UtcNow<nextCheck)continue;nextCheck=DateTime.UtcNow.AddMilliseconds(250);
                if(!ParentAlive(job)){cancelled=true;reason="Parent engine stopped";break;}
                var current=InputBridge.Snapshot(root);
                var cancel=PulseIO.Read(Path.Combine(root,"pulse-cancel-"+id+".json"));
                if(!current.Known||current.LastUtc>baseline){cancelled=true;reason="User activity";break;}
                if(s.Revision!=PulseSettings.Load(root).Revision){cancelled=true;reason="Settings changed";break;}
                if(cancel.Count>0){cancelled=true;reason=PulseIO.Text(cancel,"reason");break;}
                if(s.MouseEnabled&&mouseResult=="pending")foreach(int session in initial.MouseSessions){var ack=PulseIO.Read(Path.Combine(root,"activity","activity-"+session+".json"));if(PulseIO.Text(ack,"mouseAckId")==id)mouseResult=PulseIO.Text(ack,"mouseAckStatus");}
                PulseIO.Save(Path.Combine(root,"pulse-progress.json"),new {time=DateTimeOffset.Now.ToString("o"),id=id,elapsedSeconds=Math.Round(watch.Elapsed.TotalSeconds,1),durationSeconds=s.DurationSeconds,cpuPercent=s.CpuPercent,gpuPercent=s.GpuPercent,gpu=gpuResult,mouse=mouseResult});
            }
            stop.Set();foreach(var thread in threads)if(!thread.Join(2500))throw new TimeoutException("Load worker did not stop in time");
            if(s.MouseEnabled&&mouseResult=="pending")foreach(int session in initial.MouseSessions){var ack=PulseIO.Read(Path.Combine(root,"activity","activity-"+session+".json"));if(PulseIO.Text(ack,"mouseAckId")==id)mouseResult=PulseIO.Text(ack,"mouseAckStatus");}
            bool partial=gpuError.Length>0||(s.MouseEnabled&&mouseResult!="moved");
            string result=cancelled?"cancelled":partial?"partial":"completed";
            double measured=100*(Process.GetCurrentProcess().TotalProcessorTime.TotalSeconds-cpuStart)/Math.Max(0.1,watch.Elapsed.TotalSeconds)/Environment.ProcessorCount;
            PulseIO.Save(Path.Combine(root,"pulse-last.json"),new {time=DateTimeOffset.Now.ToString("o"),id=id,status=result,reason=reason,durationSeconds=Math.Round(watch.Elapsed.TotalSeconds,2),cpuEnabled=s.CpuEnabled,cpuTarget=s.CpuPercent,measuredCpuPercent=Math.Round(measured,1),gpu=gpuResult,gpuTarget=s.GpuPercent,gpuBatches=batches,gpuError=gpuError,mouse=mouseResult,memory=memoryResult,disk=diskResult});
            PulseIO.Log(root,"Pulse "+result+"; "+Math.Round(watch.Elapsed.TotalSeconds,2)+" s; mouse="+mouseResult+"; GPU="+gpuResult+(gpuError.Length>0?" ("+gpuError+")":"")+"; "+reason);return cancelled?2:0;
        }catch(OperationCanceledException e){PulseIO.Save(Path.Combine(root,"pulse-last.json"),new {time=DateTimeOffset.Now.ToString("o"),id=id,status="cancelled",reason=e.Message,durationSeconds=watch.Elapsed.TotalSeconds});return 2;}
        catch(Exception e){PulseIO.Save(Path.Combine(root,"pulse-last.json"),new {time=DateTimeOffset.Now.ToString("o"),id=id,status="error",reason=e.Message,durationSeconds=watch.Elapsed.TotalSeconds});PulseIO.Log(root,"Pulse failed: "+e.Message);return 1;}
        finally{stop.Set();GC.KeepAlive(sink);}
    }
}

public static class PulseEngine {
    [DllImport("kernel32.dll")]static extern uint SetThreadExecutionState(uint flags);
    [DllImport("kernel32.dll")]static extern ulong GetTickCount64();
    static Process child;static PulseJob job;static DateTime started,lastAttempt=DateTime.MinValue;static string previousCode="";
    static DateTime LastEnd(string root){return PulseIO.Time(PulseIO.Read(Path.Combine(root,"pulse-state.json")),"lastSuccessUtc");}
    public static int Run(string root) {
        bool created;using(var mutex=new Mutex(true,@"Global\PCPulseGuardCore-"+PulseIO.Key(root),out created)) {
            if(!created)return 0;
            Directory.CreateDirectory(Path.Combine(root,"activity"));PulseIO.Log(root,AppInfo.Name+" "+AppInfo.Version+" started; generic input/CPU/GPU rules.");
            try {using(var monitor=new LoadMonitor())while(true){var clock=Stopwatch.StartNew();try{Tick(root,monitor);}catch(Exception e){PulseIO.Log(root,"Scheduler: "+e.Message);}Thread.Sleep(Math.Max(20,1000-(int)clock.ElapsedMilliseconds));}}
            finally{if(child!=null)try{child.Kill();}catch{}SetThreadExecutionState(0x80000000);}
        }
    }
    static void Tick(string root,LoadMonitor monitor) {
        var s=PulseSettings.Load(root);DateTime now=DateTime.UtcNow;
        uint wake=0x80000000;if(s.Enabled&&s.KeepSystemAwake)wake|=1;if(s.Enabled&&s.KeepDisplayAwake)wake|=3;bool wakeOk=SetThreadExecutionState(wake)!=0;
        if(child!=null&&child.HasExited) {
            if(child.ExitCode==0)PulseIO.Save(Path.Combine(root,"pulse-state.json"),new {lastSuccessUtc=DateTime.UtcNow.ToString("o")});
            string oldId=job.Id;child.Dispose();child=null;job=null;
            foreach(string name in new string[]{"pulse-job-"+oldId+".json","pulse-cancel-"+oldId+".json"})try{File.Delete(Path.Combine(root,name));}catch{}
        }
        var input=InputBridge.Snapshot(root);var load=monitor.Sample(child==null?0:child.Id);DateTime lastEnd=LastEnd(root);
        var decision=PulseDecision.Evaluate(s,input.Known,input.IdleSeconds,load.CpuKnown,load.CpuPercent,load.GpuKnown,load.GpuPercent,lastEnd,now);
        if(child!=null) {
            string cancel="";
            if(!s.Enabled||s.Revision!=job.Revision)cancel="Settings changed";
            else if(!input.Known||input.LastUtc>DateTime.Parse(job.InputUtc).ToUniversalTime())cancel="User activity";
            else if(s.SkipHighLoad&&load.CpuKnown&&(load.ExternalCpuPercent>s.CpuBusyPercent||(load.ExternalGpuKnown&&load.ExternalGpuPercent>s.GpuBusyPercent)))cancel="Other CPU/GPU workload became busy";
            else if(s.GpuEnabled&&load.TemperatureC>=85)cancel="GPU temperature protection";
            if(cancel.Length>0)PulseIO.Save(Path.Combine(root,"pulse-cancel-"+job.Id+".json"),new {reason=cancel});
            if((now-started).TotalSeconds>job.Settings.DurationSeconds+10){child.Kill();PulseIO.Log(root,"Pulse exceeded its configured duration plus shutdown grace; child stopped.");}
            decision=new PulseDecision{Code="pulsing",Reason=cancel.Length>0?"Stopping heartbeat: "+cancel:"Heartbeat running",RemainingSeconds=Math.Max(0,job.Settings.DurationSeconds-(now-started).TotalSeconds)};
        }else if(decision.Code=="ready"&&s.GpuEnabled&&load.TemperatureC>=85)decision=new PulseDecision{Code="temperature",Reason="GPU temperature protection; heartbeat deferred",RemainingSeconds=-1};
        if(child==null&&decision.Code=="ready"&&(now-lastAttempt).TotalSeconds>=5) {
            lastAttempt=now;started=now;job=new PulseJob{Id=Guid.NewGuid().ToString("N"),ParentPid=Process.GetCurrentProcess().Id,ParentStartedUtc=Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("o"),InputUtc=input.LastUtc.ToString("o"),Revision=s.Revision,Settings=s};
            PulseIO.Save(Path.Combine(root,"pulse-job-"+job.Id+".json"),job);
            var p=new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,AppInfo.CoreFile),"--worker "+job.Id+" --root \""+root+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root};child=Process.Start(p);
            decision=new PulseDecision{Code="pulsing",Reason="Heartbeat running",RemainingSeconds=s.DurationSeconds};PulseIO.Log(root,"Pulse started; CPU="+(s.CpuEnabled?s.CpuPercent+"%":"off")+"; GPU="+(s.GpuEnabled?s.GpuPercent+"%":"off")+"; mouse="+s.MouseEnabled);
        }
        if(previousCode!=decision.Code){PulseIO.Log(root,"State="+decision.Code+"; CPU="+load.CpuPercent.ToString("0.0")+"%; GPU="+(load.GpuKnown?load.GpuPercent.ToString("0.0")+"%":"unavailable"));previousCode=decision.Code;}
        PulseIO.Save(Path.Combine(root,"pulse-status.json"),new {version=AppInfo.Version,time=DateTimeOffset.Now.ToString("o"),pid=Process.GetCurrentProcess().Id,code=decision.Code,reason=decision.Reason,remainingSeconds=decision.RemainingSeconds,idleKnown=input.Known,humanIdleSeconds=Math.Round(input.IdleSeconds,2),cpuKnown=load.CpuKnown,cpuPercent=Math.Round(load.CpuPercent,1),gpuKnown=load.GpuKnown,gpuPercent=Math.Round(load.GpuPercent,1),gpuSource=load.GpuSource,externalCpuPercent=Math.Round(load.ExternalCpuPercent,1),externalGpuKnown=load.ExternalGpuKnown,externalGpuPercent=Math.Round(load.ExternalGpuPercent,1),gpuTemperatureC=load.TemperatureC,freeVramMiB=load.FreeMiB,running=child!=null,workerPid=child==null?0:child.Id,jobId=job==null?null:job.Id,lastSuccessUtc=lastEnd==DateTime.MinValue?null:lastEnd.ToString("o"),wakeRequestOk=wakeOk,settings=s,settingsRevision=s.Revision});
        PulseIO.Save(Path.Combine(root,"heartbeat.json"),new {time=DateTimeOffset.Now.ToString("o"),pid=Process.GetCurrentProcess().Id,uptimeSeconds=GetTickCount64()/1000,version=AppInfo.Version,wakeRequestActive=s.Enabled&&s.KeepSystemAwake});
    }
}

public static class WatchdogProgram {
    [STAThread]public static int Main(string[] args) {
        PulseIO.SetRoot(args);
        try {
            if(Array.IndexOf(args,"--activity")>=0)return InputBridge.Run(PulseIO.Root);
            int worker=Array.IndexOf(args,"--worker");if(worker>=0&&worker+1<args.Length)return PulseWorker.Run(PulseIO.Root,args[worker+1]);
            return PulseEngine.Run(PulseIO.Root);
        }catch(Exception e){PulseIO.Log(PulseIO.Root,"Fatal: "+e.Message);return 1;}
    }
}
