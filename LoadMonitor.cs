using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

public sealed class LoadSample {
    public bool CpuKnown,GpuKnown,ExternalGpuKnown;
    public double CpuPercent,GpuPercent,ExternalCpuPercent,ExternalGpuPercent;
    public double TemperatureC=-1,FreeMiB=-1;
    public string GpuSource="不可用";
}

public sealed class LoadMonitor:IDisposable {
    [DllImport("kernel32.dll")]static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhOpenQuery(string source,IntPtr user,out IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhAddEnglishCounter(IntPtr query,string path,IntPtr user,out IntPtr counter);
    [DllImport("pdh.dll")]static extern uint PdhCollectQueryData(IntPtr query);
    [DllImport("pdh.dll")]static extern uint PdhCloseQuery(IntPtr query);
    [DllImport("pdh.dll",CharSet=CharSet.Unicode)]static extern uint PdhGetFormattedCounterArrayW(IntPtr counter,uint format,ref uint bytes,out uint count,IntPtr buffer);
    [StructLayout(LayoutKind.Sequential)]struct CounterItem{public IntPtr Name;public uint Status;public double Value;}
    [DllImport("nvml.dll")]static extern int nvmlInit_v2();
    [DllImport("nvml.dll")]static extern int nvmlShutdown();
    [DllImport("nvml.dll")]static extern int nvmlDeviceGetHandleByIndex_v2(uint index,out IntPtr device);
    [DllImport("nvml.dll")]static extern int nvmlDeviceGetUtilizationRates(IntPtr device,out Util rates);
    [DllImport("nvml.dll")]static extern int nvmlDeviceGetTemperature(IntPtr device,uint sensor,out uint temperature);
    [DllImport("nvml.dll")]static extern int nvmlDeviceGetMemoryInfo(IntPtr device,out Memory memory);
    [StructLayout(LayoutKind.Sequential)]struct Util{public uint Gpu,Memory;}
    [StructLayout(LayoutKind.Sequential)]struct Memory{public ulong Total,Free,Used;}
    IntPtr query,counter;long previousIdle,previousKernel,previousUser;bool ready;DateTime previousAt;
    int previousPid;double previousOwn;
    public LoadMonitor(){try{if(PdhOpenQuery(null,IntPtr.Zero,out query)==0&&PdhAddEnglishCounter(query,@"\GPU Engine(*)\Utilization Percentage",IntPtr.Zero,out counter)==0)PdhCollectQueryData(query);}catch{}}
    public LoadSample Sample(int excludePid) {
        var s=new LoadSample();long idle,kernel,user;DateTime now=DateTime.UtcNow;
        if(GetSystemTimes(out idle,out kernel,out user)) {
            double total=(kernel-previousKernel)+(user-previousUser);
            if(ready&&total>0){s.CpuKnown=true;s.CpuPercent=Math.Max(0,Math.Min(100,100*(total-(idle-previousIdle))/total));s.ExternalCpuPercent=s.CpuPercent;}
            double own=0;if(excludePid>0){try{using(var p=Process.GetProcessById(excludePid))own=p.TotalProcessorTime.TotalSeconds;}catch{}}
            if(s.CpuKnown&&excludePid>0){double old=previousPid==excludePid?previousOwn:0;s.ExternalCpuPercent=Math.Max(0,s.CpuPercent-100*Math.Max(0,own-old)*10000000/total);}
            previousIdle=idle;previousKernel=kernel;previousUser=user;ready=true;previousAt=now;previousOwn=own;previousPid=excludePid;
        }
        bool nvml=false;
        try {
            if(nvmlInit_v2()==0) {
                nvml=true;IntPtr device;Util u;uint t;Memory m;
                if(nvmlDeviceGetHandleByIndex_v2(0,out device)==0) {
                    if(nvmlDeviceGetUtilizationRates(device,out u)==0){s.GpuKnown=true;s.GpuPercent=u.Gpu;s.GpuSource="NVIDIA";}
                    if(nvmlDeviceGetTemperature(device,0,out t)==0)s.TemperatureC=t;
                    if(nvmlDeviceGetMemoryInfo(device,out m)==0)s.FreeMiB=m.Free/1048576.0;
                }
            }
        }catch{}finally{if(nvml)nvmlShutdown();}
        if(query!=IntPtr.Zero&&counter!=IntPtr.Zero) {
            try {
                PdhCollectQueryData(query);uint bytes=0,count;uint code=PdhGetFormattedCounterArrayW(counter,0x200,ref bytes,out count,IntPtr.Zero);
                if(bytes>0&&bytes<=16777216) {
                    IntPtr buffer=Marshal.AllocHGlobal((int)bytes);
                    try {
                        if(PdhGetFormattedCounterArrayW(counter,0x200,ref bytes,out count,buffer)==0) {
                            var all=new Dictionary<string,double>();var external=new Dictionary<string,double>();int size=Marshal.SizeOf(typeof(CounterItem));int valid=0;
                            for(int i=0;i<count;i++) {
                                var item=(CounterItem)Marshal.PtrToStructure(IntPtr.Add(buffer,i*size),typeof(CounterItem));if(item.Status>1||Double.IsNaN(item.Value))continue;
                                string name=Marshal.PtrToStringUni(item.Name);var match=Regex.Match(name,@"^pid_(\d+)_(.+)$");if(!match.Success)continue;
                                int pid;if(!Int32.TryParse(match.Groups[1].Value,out pid))continue;string engine=match.Groups[2].Value;double v=Math.Max(0,item.Value);double prev;
                                all.TryGetValue(engine,out prev);all[engine]=prev+v;if(pid!=excludePid){external.TryGetValue(engine,out prev);external[engine]=prev+v;}valid++;
                            }
                            if(valid>0){double max=0,other=0;foreach(double v in all.Values)max=Math.Max(max,v);foreach(double v in external.Values)other=Math.Max(other,v);s.GpuKnown=true;s.ExternalGpuKnown=true;s.GpuPercent=Math.Min(100,max);s.ExternalGpuPercent=Math.Min(100,other);s.GpuSource="Windows GPU Engine";}
                        }
                    }finally{Marshal.FreeHGlobal(buffer);}
                }
            }catch{}
        }
        if(!s.ExternalGpuKnown&&excludePid==0){s.ExternalGpuPercent=s.GpuPercent;s.ExternalGpuKnown=s.GpuKnown;}
        return s;
    }
    public void Dispose(){if(query!=IntPtr.Zero){PdhCloseQuery(query);query=IntPtr.Zero;}}
}
