using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

[assembly:System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]

public sealed class PulseSettings {
    public int Version=2;
    public bool Enabled=true, MouseEnabled=false, CpuEnabled=true, GpuEnabled=false;
    public double IdleValue=10, RepeatValue=15, DurationValue=5;
    public string IdleUnit="min", RepeatUnit="min", DurationUnit="s";
    public int CpuPercent=5, GpuPercent=5;
    public int MousePixels=1;
    public bool SkipHighLoad=true;
    public int CpuBusyPercent=20, GpuBusyPercent=20;
    public bool KeepSystemAwake=true, KeepDisplayAwake=false, MemoryEnabled=false, DiskEnabled=false;
    [ScriptIgnore] public double IdleSeconds {get{return Seconds(IdleValue,IdleUnit,604800);}}
    [ScriptIgnore] public double RepeatSeconds {get{return Seconds(RepeatValue,RepeatUnit,604800);}}
    [ScriptIgnore] public double DurationSeconds {get{return Seconds(DurationValue,DurationUnit,86400);}}
    [ScriptIgnore] public bool HasActions {get{return MouseEnabled || CpuEnabled || GpuEnabled || MemoryEnabled || DiskEnabled;}}
    public static double Factor(string unit) {return unit=="h"?3600:unit=="min"?60:1;}
    public static string UnitName(string unit) {return unit=="h"?"小时":unit=="min"?"分钟":"秒";}
    public static double Seconds(double value,string unit,double max) {if(Double.IsNaN(value)||Double.IsInfinity(value))return 1;return Math.Max(1,Math.Min(max,Math.Round(value*Factor(unit))));}
    public void Normalize() {
        IdleUnit=Unit(IdleUnit);RepeatUnit=Unit(RepeatUnit);DurationUnit=Unit(DurationUnit);
        IdleValue=IdleSeconds/Factor(IdleUnit);RepeatValue=RepeatSeconds/Factor(RepeatUnit);DurationValue=DurationSeconds/Factor(DurationUnit);
        CpuPercent=Limit(CpuPercent);GpuPercent=Limit(GpuPercent);CpuBusyPercent=Limit(CpuBusyPercent);GpuBusyPercent=Limit(GpuBusyPercent);MousePixels=Math.Max(1,Math.Min(10000,MousePixels));Version=2;
    }
    static string Unit(string unit) {return unit=="h"||unit=="min"?unit:"s";}
    static int Limit(int n){return Math.Max(1,Math.Min(100,n));}
    [ScriptIgnore] public string Revision {get {
        // Use an explicit field order: serializer reflection order can differ after WinForms initializes.
        object[] values={Version,Enabled,MouseEnabled,CpuEnabled,GpuEnabled,IdleValue,RepeatValue,DurationValue,IdleUnit,RepeatUnit,DurationUnit,CpuPercent,GpuPercent,MousePixels,SkipHighLoad,CpuBusyPercent,GpuBusyPercent,KeepSystemAwake,KeepDisplayAwake,MemoryEnabled,DiskEnabled};
        using(var sha=SHA256.Create()){return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(PulseIO.Json.Serialize(values)))).Replace("-","");}
    }}
    public PulseSettings Copy(){return PulseIO.Json.Deserialize<PulseSettings>(PulseIO.Json.Serialize(this));}
    public string TimeText(double value,string unit){return value.ToString("0.####",CultureInfo.CurrentCulture)+" "+UnitName(unit);}
    public static PulseSettings Load(string root) {
        try {
            string text=PulseIO.ReadText(Path.Combine(root,"activity","preferences.json"),16384);
            var raw=PulseIO.Json.Deserialize<Dictionary<string,object>>(text);
            PulseSettings s;
            if(raw.ContainsKey("idleMinutes")) {
                s=new PulseSettings();s.IdleValue=PulseIO.Number(raw,"idleMinutes",10);s.RepeatValue=PulseIO.Number(raw,"intervalMinutes",15);
                s.CpuPercent=10;s.GpuEnabled=true;s.GpuPercent=10;s.MemoryEnabled=true;s.DiskEnabled=true;
            }else s=PulseIO.Json.Deserialize<PulseSettings>(text);
            s.Normalize();return s;
        }catch{return new PulseSettings();}
    }
    public void Save(string root){Normalize();PulseIO.Save(Path.Combine(root,"activity","preferences.json"),this);}
}

public static class PulseIO {
    public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    public static string Root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
    public static void SetRoot(string[] args) {for(int i=0;i+1<args.Length;i++)if(args[i]=="--root")Root=Path.GetFullPath(args[i+1]).TrimEnd(Path.DirectorySeparatorChar);}
    public static string ReadText(string path,int max) {
        if(new FileInfo(path).Length>max)throw new IOException("Data file exceeds its size limit");
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var r=new StreamReader(stream))return r.ReadToEnd();
    }
    public static Dictionary<string,object> Read(string path) {try{return Json.Deserialize<Dictionary<string,object>>(ReadText(path,131072));}catch{return new Dictionary<string,object>();}}
    public static void Save(string path,object data) {
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+"."+Process.GetCurrentProcess().Id+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {File.WriteAllText(temp,Json.Serialize(data),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}
        finally {if(File.Exists(temp))File.Delete(temp);}
    }
    public static object Get(Dictionary<string,object> d,string key){object v;return d!=null&&d.TryGetValue(key,out v)?v:null;}
    public static double Number(Dictionary<string,object> d,string key,double fallback){try {object v=Get(d,key);return v==null?fallback:Convert.ToDouble(v,CultureInfo.InvariantCulture);}catch{return fallback;}}
    public static bool Flag(Dictionary<string,object> d,string key){try{return Convert.ToBoolean(Get(d,key));}catch{return false;}}
    public static string Text(Dictionary<string,object> d,string key){return Convert.ToString(Get(d,key),CultureInfo.InvariantCulture);}
    public static DateTime Time(Dictionary<string,object> d,string key){DateTime t;return DateTime.TryParse(Text(d,key),CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out t)?t.ToUniversalTime():DateTime.MinValue;}
    public static Dictionary<string,object> Child(Dictionary<string,object> d,string key){return Get(d,key) as Dictionary<string,object> ?? new Dictionary<string,object>();}
    public static void Log(string root,string text) {
        try {string path=Path.Combine(root,"watchdog.log");if(File.Exists(path)&&new FileInfo(path).Length>2097152){if(File.Exists(path+".1"))File.Delete(path+".1");File.Move(path,path+".1");}File.AppendAllText(path,DateTimeOffset.Now.ToString("o")+" "+text+Environment.NewLine,Encoding.UTF8);}catch{}
    }
    public static string Key(string root) {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant())),0,8).Replace("-","");}
    public static string Duration(double seconds) {if(seconds<0||Double.IsNaN(seconds))return "--:--";long s=(long)Math.Ceiling(Math.Min(seconds,315576000000));return s>=3600?String.Format("{0}:{1:00}:{2:00}",s/3600,s/60%60,s%60):String.Format("{0:00}:{1:00}",s/60,s%60);}
}

public sealed class PulseDecision {
    public string Code,Reason;public double RemainingSeconds;
    public static PulseDecision Evaluate(PulseSettings s,bool inputKnown,double idle,bool cpuKnown,double cpu,bool gpuKnown,double gpu,DateTime lastEnd,DateTime now) {
        if(!s.Enabled)return New("disabled","Keep-alive is disabled",-1);
        if(!s.HasActions)return New("no_actions","Select at least one heartbeat action",-1);
        if(!inputKnown)return New("activity_unknown","Waiting for reliable input timing",-1);
        if(idle<s.IdleSeconds)return New("idle_wait","Waiting for user inactivity",s.IdleSeconds-idle);
        double repeat=lastEnd==DateTime.MinValue?0:Math.Max(0,s.RepeatSeconds-(now-lastEnd).TotalSeconds);
        if(repeat>0)return New("cooldown","Waiting for the next heartbeat",repeat);
        if(s.SkipHighLoad && !cpuKnown)return New("sampling","Sampling CPU utilization",-1);
        if(s.SkipHighLoad && (cpu>s.CpuBusyPercent || (gpuKnown && gpu>s.GpuBusyPercent)))return New("busy","Deferring during high compute load",-1);
        return New("ready","Ready for a heartbeat",0);
    }
    static PulseDecision New(string code,string reason,double remain){return new PulseDecision{Code=code,Reason=reason,RemainingSeconds=remain};}
}
