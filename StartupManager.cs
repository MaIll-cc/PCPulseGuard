using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;

public sealed class StartupState {
    public bool Known,Enabled,Silent=true,Installed,Mixed;
    public string Error="";
}
public static class StartupManager {
    const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string EntryName(string root){return AppInfo.Name+"-"+PulseIO.Key(root);}
    public static string Command(string executable,string root,bool silent){return "\""+executable+"\" "+(silent?"--tray":"--show")+" --root \""+root+"\"";}
    public static bool Installed(string root){var marker=PulseIO.Read(Path.Combine(root,"installation.json"));return PulseIO.Flag(marker,"machine")&&PulseIO.Text(marker,"product")==AppInfo.Name;}
    static void Release(object value){if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value);}
    public static StartupState Read(string root){var state=new StartupState{Installed=Installed(root),Silent=UiPreferences.Load(root).SilentStartup};try{
        if(!state.Installed){using(var key=Registry.CurrentUser.OpenSubKey(RunKey)){string value=key==null?null:key.GetValue(EntryName(root)) as string;state.Enabled=!String.IsNullOrEmpty(value);if(state.Enabled){string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,AppInfo.TrayFile);if(value!=Command(exe,root,true)&&value!=Command(exe,root,false))throw new InvalidOperationException("Startup entry belongs to another executable.");state.Silent=value==Command(exe,root,true);}}state.Known=true;return state;}
        object service=null,folder=null;try{service=Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));((dynamic)service).Connect();folder=((dynamic)service).GetFolder("\\");int enabled=0;string prefix=PulseIO.Text(PulseIO.Read(Path.Combine(root,"installation.json")),"taskPrefix");
            foreach(string suffix in new[]{"Watchdog","Activity","Tray"}){object task=null,definition=null,actions=null,action=null;try{task=((dynamic)folder).GetTask(prefix+"-"+suffix);if(((dynamic)task).Enabled)enabled++;definition=((dynamic)task).Definition;actions=((dynamic)definition).Actions;action=((dynamic)actions).Item(1);string expected=Path.Combine(root,suffix=="Tray"?AppInfo.TrayFile:AppInfo.CoreFile);if(!String.Equals((string)((dynamic)action).Path,expected,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Unexpected startup task executable.");if(suffix=="Tray")state.Silent=((string)((dynamic)action).Arguments).StartsWith("--tray",StringComparison.Ordinal);}finally{Release(action);Release(actions);Release(definition);Release(task);}}
            state.Enabled=enabled==3;state.Mixed=enabled>0&&enabled<3;state.Known=true;
        }finally{Release(folder);Release(service);}return state;
    }catch(Exception e){state.Error=e.Message;PulseIO.Log(Path.Combine(root,"activity"),"Startup state: "+e.Message);return state;}}
    public static void SetPortable(string root,bool enabled,bool silent){if(Installed(root))throw new InvalidOperationException("Use the installed-task configuration.");var current=Read(root);if(!current.Known)throw new InvalidOperationException(current.Error);using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(enabled)key.SetValue(EntryName(root),Command(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,AppInfo.TrayFile),root,silent),RegistryValueKind.String);else key.DeleteValue(EntryName(root),false);}var pref=UiPreferences.Load(root);pref.SilentStartup=silent;pref.Save(root);}
    public static Task<string> ApplyAsync(string root,bool enabled,bool silent,bool startOnly){return Task.Run(delegate{try{
        if(!Installed(root)){if(startOnly)return "ok";SetPortable(root,enabled,silent);return "ok";}
        string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"SetStartup.ps1");if(!File.Exists(script))throw new FileNotFoundException("Startup configuration script is missing.");
        string args="-NoProfile -ExecutionPolicy Bypass -File \""+script+"\" -InstallRoot \""+root+"\" -Mode "+(startOnly?"Start":"Apply")+" -Enabled "+(enabled?"On":"Off")+" -Silent "+(silent?"On":"Off");
        using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"WindowsPowerShell\v1.0\powershell.exe"),args){UseShellExecute=true,Verb="runas",WindowStyle=ProcessWindowStyle.Hidden})){process.WaitForExit();if(process.ExitCode!=0){PulseIO.Log(Path.Combine(root,"activity"),"Startup change failed; "+PulseIO.ReadText(Path.Combine(root,"activity","startup-last-error.txt"),8192));return "failed";}}return "ok";
    }catch(System.ComponentModel.Win32Exception e){if(e.NativeErrorCode==1223)return "cancelled";PulseIO.Log(Path.Combine(root,"activity"),"Startup: "+e.Message);return "failed";}catch(Exception e){PulseIO.Log(Path.Combine(root,"activity"),"Startup: "+e.Message);return "failed";}});}
}
