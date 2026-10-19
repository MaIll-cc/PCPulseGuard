using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

public static class PulseChecks {
    public static int Run(string[] args) {
        var results=new List<object>();var s=new PulseSettings();DateTime now=DateTime.UtcNow;
        Action<string,bool> assert=delegate(string name,bool passed){if(!passed)throw new Exception("Check failed: "+name);results.Add(new {name=name,passed=true});};
        Func<PulseSettings,double,double,double,bool,DateTime,string> state=delegate(PulseSettings p,double idle,double cpu,double gpu,bool known,DateTime last){return PulseDecision.Evaluate(p,known,idle,true,cpu,true,gpu,last,now).Code;};
        assert("seconds conversion",PulseSettings.Seconds(17,"s",604800)==17);
        assert("minutes conversion",PulseSettings.Seconds(2.5,"min",604800)==150);
        assert("hours conversion",PulseSettings.Seconds(1.5,"h",604800)==5400);
        assert("server uptime beyond seven days",PulseIO.Duration(900000)=="250:00:00");
        assert("seven complete language catalogs and placeholders",L.Validate()>100&&L.Codes.Length==7);
        foreach(string code in L.Codes){L.Set(code);assert("localized state "+code,!String.IsNullOrEmpty(L.State("busy")));}L.Set("en");
        assert("semantic version parsing",UpdateChecker.ParseVersion("v1.2.3")>UpdateChecker.ParseVersion("v0.9.9")&&UpdateChecker.ParseVersion("v0.3.0")==UpdateChecker.ParseVersion(AppInfo.Version));
        assert("reject malformed and prerelease versions",UpdateChecker.ParseVersion("v0.4.0-beta")==null&&UpdateChecker.ParseVersion("999999999999.1.0")==null&&UpdateChecker.ParseVersion("& calc.exe")==null);
        string release=AppInfo.RepositoryUrl+"/releases/tag/v0.4.0";
        assert("official release URLs only",UpdateChecker.ReleaseUrlAllowed(release)&&!UpdateChecker.ReleaseUrlAllowed("https://evil.example/releases/tag/v1.0.0")&&!UpdateChecker.ReleaseUrlAllowed(release.Replace("https:","http:"))&&!UpdateChecker.ReleaseUrlAllowed("https://github.com/other/project/releases/tag/v1.0.0")&&!UpdateChecker.ReleaseUrlAllowed("file:///C:/Windows/calc.exe"));
        var releaseData=new Dictionary<string,object>{{"tag_name","v0.4.0"},{"html_url",release}};
        assert("new stable release detected",UpdateChecker.Parse(releaseData).Available);releaseData["tag_name"]="v"+AppInfo.Version;
        assert("current release does not offer downgrade",!UpdateChecker.Parse(releaseData).Available);releaseData["prerelease"]=true;
        assert("prereleases excluded",UpdateChecker.Parse(releaseData).Code=="no_release");
        assert("startup command quotes executable and root",StartupManager.Command(@"C:\Test Folder\PCPulseGuard.exe",@"C:\Data Folder",true)=="\"C:\\Test Folder\\PCPulseGuard.exe\" --tray --root \"C:\\Data Folder\"");
        assert("normal startup explicitly opens window",StartupManager.Command("app.exe","data",false)=="\"app.exe\" --show --root \"data\"");
        assert("mouse defaults to one pixel",s.MousePixels==1);
        s.MousePixels=20000;s.Normalize();assert("mouse pixel bounds",s.MousePixels==10000);s.MousePixels=37;
        s.Enabled=false;assert("master off",state(s,99999,0,0,true,DateTime.MinValue)=="disabled");s.Enabled=true;
        s.CpuEnabled=false;assert("no actions",state(s,99999,0,0,true,DateTime.MinValue)=="no_actions");s.CpuEnabled=true;
        assert("unknown input",state(s,99999,0,0,false,DateTime.MinValue)=="activity_unknown");
        assert("idle is primary",state(s,20,0,0,true,DateTime.MinValue)=="idle_wait");
        assert("resident models do not affect decision",state(s,601,0,0,true,DateTime.MinValue)=="ready");
        assert("CPU busy guard",state(s,601,80,0,true,DateTime.MinValue)=="busy");
        assert("GPU busy guard",state(s,601,0,80,true,DateTime.MinValue)=="busy");
        s.SkipHighLoad=false;assert("busy guard optional",state(s,601,99,99,true,DateTime.MinValue)=="ready");s.SkipHighLoad=true;
        assert("cooldown",state(s,601,0,0,true,now.AddSeconds(-2))=="cooldown");
        s.CpuBusyPercent=90;s.GpuBusyPercent=90;assert("custom thresholds",state(s,601,80,80,true,DateTime.MinValue)=="ready");
        s.DurationValue=2;s.DurationUnit="h";assert("duration uses hours",s.DurationSeconds==7200);
        s.RepeatValue=3;s.RepeatUnit="s";assert("repeat uses seconds",s.RepeatSeconds==3);
        s.IdleValue=Double.PositiveInfinity;s.CpuPercent=999;s.GpuPercent=-5;s.Normalize();assert("bounded settings",s.IdleSeconds==1&&s.CpuPercent==100&&s.GpuPercent==1);
        string temp=Path.Combine(Path.GetTempPath(),AppInfo.Name+"-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(temp,"activity"));
        try {
            File.WriteAllText(Path.Combine(temp,"activity","preferences.json"),"{\"idleMinutes\":10,\"intervalMinutes\":15}");var old=PulseSettings.Load(temp);assert("v0.1 preferences migrate",old.IdleSeconds==600&&old.RepeatSeconds==900&&old.GpuEnabled);
            old.IdleValue=1;old.IdleUnit="h";old.MouseEnabled=true;old.MousePixels=37;old.Save(temp);var reloaded=PulseSettings.Load(temp);assert("settings roundtrip including custom mouse distance",reloaded.IdleSeconds==3600&&reloaded.MouseEnabled&&reloaded.MousePixels==37&&reloaded.Revision==old.Revision);
            var pref=new UiPreferences{Language="fr",AutoCheckUpdates=false,SilentStartup=false};pref.Save(temp);var loaded=UiPreferences.Load(temp);assert("language and update preference persist",loaded.Language=="fr"&&!loaded.AutoCheckUpdates&&!loaded.SilentStartup);
            assert("silent startup is the fresh preference default",new UiPreferences().SilentStartup);
            assert("UI preference changes do not change pulse settings",PulseSettings.Load(temp).Revision==old.Revision);
        }finally{Directory.Delete(temp,true);}
        if(Array.IndexOf(args,"--startup-check")>=0){
            string startupRoot=Path.Combine(Path.GetTempPath(),AppInfo.Name+"-startup-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(startupRoot);
            try{
                var initial=StartupManager.Read(startupRoot);assert("fresh portable copy has no startup entry",initial.Known&&!initial.Enabled);
                StartupManager.SetPortable(startupRoot,true,true);var silent=StartupManager.Read(startupRoot);assert("portable silent startup registry roundtrip",silent.Known&&silent.Enabled&&silent.Silent);
                StartupManager.SetPortable(startupRoot,true,false);var visible=StartupManager.Read(startupRoot);assert("portable visible startup registry roundtrip",visible.Known&&visible.Enabled&&!visible.Silent);
                StartupManager.SetPortable(startupRoot,false,false);var disabled=StartupManager.Read(startupRoot);assert("portable startup removal retains silent preference",disabled.Known&&!disabled.Enabled&&!disabled.Silent);
            }finally{StartupManager.SetPortable(startupRoot,false,true);Directory.Delete(startupRoot,true);}
        }
        if(Array.IndexOf(args,"--gpu-check")>=0) {
            using(var monitor=new LoadMonitor()) {
                monitor.Sample(0);Thread.Sleep(1000);var sample=monitor.Sample(0);
                assert("CPU sensor available",sample.CpuKnown);results.Add(new {name="GPU monitor",known=sample.GpuKnown,source=sample.GpuSource,percent=sample.GpuPercent});
                var stop=new ManualResetEvent(false);using(var timer=new Timer(delegate{stop.Set();},null,2000,Timeout.Infinite))using(var gpu=new GpuPulse()){long batches=gpu.Run(5,stop);assert("Direct3D GPU pulse and result verification",batches>0);results.Add(new {gpuBatches=batches});}
            }
        }
        string output=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,Array.IndexOf(args,"--gpu-check")>=0?"checks-gpu.json":Array.IndexOf(args,"--startup-check")>=0?"checks-startup.json":"checks.json");PulseIO.Save(output,new {time=DateTimeOffset.Now.ToString("o"),checks=results.Count,results=results});Console.WriteLine(PulseIO.Json.Serialize(new {passed=results.Count,report=output}));return 0;
    }
    public static int Main(string[] args){try{return Run(args);}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
