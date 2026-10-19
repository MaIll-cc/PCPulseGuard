using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

public sealed class InputSnapshot {
    public bool Known=true;public DateTime LastUtc;public List<int> MouseSessions=new List<int>();
    public double IdleSeconds {get{return Math.Max(0,(DateTime.UtcNow-LastUtc).TotalSeconds);}}
}

public static class InputBridge {
    [StructLayout(LayoutKind.Sequential)] public struct LastInput {public uint Size,Tick;}
    [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LastInput input);
    [DllImport("kernel32.dll")] static extern ulong GetTickCount64();
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode)] static extern bool WTSEnumerateSessionsW(IntPtr server,int reserved,int version,out IntPtr sessions,out int count);
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode)] static extern bool WTSQuerySessionInformationW(IntPtr server,int session,int info,out IntPtr data,out int bytes);
    [DllImport("wtsapi32.dll")] static extern void WTSFreeMemory(IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct Session {public int Id;public IntPtr Name;public int State;}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct Info1 {
        public uint Id;public int State,Flags;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=33)]public string Station;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=21)]public string User;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=18)]public string Domain;
        public long Logon,Connect,Disconnect,Input,Current;
        public uint InBytes,OutBytes,InFrames,OutFrames,InCompressed,OutCompressed;
    }
    [StructLayout(LayoutKind.Sequential)]struct InfoEx {public uint Level;public Info1 Data;}
    public static InputSnapshot Snapshot(string root) {
        var result=new InputSnapshot{LastUtc=DateTime.UtcNow.AddMilliseconds(-(double)GetTickCount64())};
        IntPtr list;int count;if(!WTSEnumerateSessionsW(IntPtr.Zero,0,1,out list,out count)){result.Known=false;return result;}
        try {
            int size=Marshal.SizeOf(typeof(Session));
            for(int i=0;i<count;i++) {
                var session=(Session)Marshal.PtrToStructure(IntPtr.Add(list,i*size),typeof(Session));
                if(session.Id==0||session.State==6||session.State==8||session.State==9)continue;
                IntPtr data;int bytes;
                if(!WTSQuerySessionInformationW(IntPtr.Zero,session.Id,25,out data,out bytes)){result.Known=false;continue;}
                try {
                    if(bytes<Marshal.SizeOf(typeof(InfoEx))){result.Known=false;continue;}
                    var info=(InfoEx)Marshal.PtrToStructure(data,typeof(InfoEx));
                    if(String.IsNullOrEmpty(info.Data.User))continue;
                    var record=PulseIO.Read(Path.Combine(root,"activity","activity-"+session.Id+".json"));
                    DateTime seen=PulseIO.Time(record,"seenUtc"),input=PulseIO.Time(record,"inputUtc");
                    bool fresh=PulseIO.Text(record,"version")==AppInfo.Version&&PulseIO.Flag(record,"monitorHealthy")&&(DateTime.UtcNow-seen).TotalSeconds<=8 && seen<=DateTime.UtcNow.AddSeconds(2) && input<=DateTime.UtcNow.AddSeconds(2);
                    if(fresh) {
                        if(input>result.LastUtc)result.LastUtc=input;
                        if(session.State==0&&PulseIO.Flag(record,"mouseAvailable"))result.MouseSessions.Add(session.Id);
                    }else if(session.State!=4)result.Known=false;
                    else {
                        // A disconnected session cannot supply new input. Do not use WTS LastInputTime,
                        // which includes our own synthetic mouse events.
                        foreach(long tick in new long[]{info.Data.Logon,info.Data.Connect,info.Data.Disconnect})if(tick>0){DateTime at=DateTime.FromFileTimeUtc(tick);if(at<=DateTime.UtcNow&&at>result.LastUtc)result.LastUtc=at;}
                    }
                }finally{WTSFreeMemory(data);}
            }
        }finally{WTSFreeMemory(list);}
        return result;
    }
    public static int Run(string root,bool pollOnly) {
        if(Process.GetCurrentProcess().SessionId==0)return 2;
        SetProcessDPIAware();
        bool created;using(var mutex=new Mutex(true,@"Local\PCPulseGuardInput-"+PulseIO.Key(root),out created)) {
            if(!created)return 0;Application.Run(new ActivityContext(root,pollOnly));return 0;
        }
    }
}

sealed class ActivityContext:ApplicationContext {
    [DllImport("user32.dll")]static extern IntPtr OpenInputDesktop(uint flags,bool inherit,uint access);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetUserObjectInformation(IntPtr h,int index,StringBuilder value,int bytes,out int needed);
    [DllImport("user32.dll")]static extern bool CloseDesktop(IntPtr h);
    [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] data,int size);
    [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
    [StructLayout(LayoutKind.Sequential)]struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
    [StructLayout(LayoutKind.Explicit,Size=40)]struct Input{[FieldOffset(0)]public uint Type;[FieldOffset(8)]public Mouse Mouse;}
    readonly string root;readonly int session;readonly System.Windows.Forms.Timer timer;readonly InputMonitor monitor;
    DateTime lastWrite=DateTime.MinValue;string ack="",ackStatus="";int ackPixels;DateTime ackAt;
    internal ActivityContext(string directory,bool pollOnly) {
        root=directory;session=Process.GetCurrentProcess().SessionId;Directory.CreateDirectory(Path.Combine(root,"activity"));
        monitor=new InputMonitor(pollOnly);
        timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=delegate{try{Tick();}catch(Exception e){PulseIO.Log(Path.Combine(root,"activity"),"Input bridge: "+e.Message);}};timer.Start();Tick();
    }
    bool DesktopAvailable() {
        IntPtr desktop=OpenInputDesktop(0,false,1);if(desktop==IntPtr.Zero)return false;
        try{int needed;var name=new StringBuilder(64);return GetUserObjectInformation(desktop,2,name,128,out needed)&&name.ToString()=="Default";}finally{CloseDesktop(desktop);}
    }
    void Tick() {
        bool available=DesktopAvailable();var observed=monitor.Read();DateTime lastInput=observed.LastUtc;
        var request=PulseIO.Read(Path.Combine(root,"activity","mouse-request-"+session+".json"));
        string id=PulseIO.Text(request,"id");
        if(id.Length==32&&id!=ack&&PulseIO.Time(request,"expiresUtc")>=DateTime.UtcNow&&PulseIO.Time(request,"expiresUtc")<=DateTime.UtcNow.AddSeconds(10)) {
            ack=id;ackPixels=0;ackAt=DateTime.UtcNow;var settings=PulseSettings.Load(root);
            if(!settings.Enabled||!settings.MouseEnabled||settings.Revision!=PulseIO.Text(request,"revision")){ackStatus="settings_changed";PulseIO.Log(Path.Combine(root,"activity"),"Mouse request settings mismatch: current="+settings.Revision+" requested="+PulseIO.Text(request,"revision")+" enabled="+settings.Enabled+" mouse="+settings.MouseEnabled);}
            else if(!observed.Healthy)ackStatus="input_monitor_unavailable";
            else if((DateTime.UtcNow-lastInput).TotalSeconds<settings.IdleSeconds)ackStatus="user_active";
            else if(!available)ackStatus="locked";
            else if(GetAsyncKeyState(1)<0||GetAsyncKeyState(2)<0||GetAsyncKeyState(4)<0)ackStatus="mouse_button_down";
            else {
                Point p;if(!GetCursorPos(out p))ackStatus="unavailable";
                else {
                    var bounds=Screen.FromPoint(new System.Drawing.Point(p.X,p.Y)).Bounds;
                    int right=Math.Max(0,bounds.Right-1-p.X),left=Math.Max(0,p.X-bounds.Left);
                    int dx=right>=left?Math.Min(settings.MousePixels,right):-Math.Min(settings.MousePixels,left);
                    var desktop=SystemInformation.VirtualScreen;
                    uint ownTick=unchecked((uint)Environment.TickCount);monitor.Own(ownTick);
                    // Absolute coordinates avoid Windows pointer acceleration changing the requested pixel distance.
                    var a=new Input{Type=0,Mouse=new Mouse{X=Absolute(p.X+dx,desktop.Left,desktop.Width),Y=Absolute(p.Y,desktop.Top,desktop.Height),Flags=0xE001,Time=ownTick,Extra=new UIntPtr(InputMonitor.OwnMarker)}};
                    var b=new Input{Type=0,Mouse=new Mouse{X=Absolute(p.X,desktop.Left,desktop.Width),Y=Absolute(p.Y,desktop.Top,desktop.Height),Flags=0xE001,Time=ownTick,Extra=new UIntPtr(InputMonitor.OwnMarker)}};
                    ackStatus=SendInput(2,new Input[]{a,b},Marshal.SizeOf(typeof(Input)))==2?"moved":"blocked";
                    if(ackStatus=="moved"){
                        ackPixels=Math.Abs(dx);
                    }
                }
            }
            lastWrite=DateTime.MinValue;
        }
        observed=monitor.Read();lastInput=observed.LastUtc;
        if((DateTime.UtcNow-lastWrite).TotalSeconds>=1||lastInput>lastWrite) {
            PulseIO.Save(Path.Combine(root,"activity","activity-"+session+".json"),new {version=AppInfo.Version,sessionId=session,inputUtc=lastInput.ToString("o"),seenUtc=DateTime.UtcNow.ToString("o"),inputSource=observed.Source,mouseAvailable=available&&observed.Healthy,mouseAckId=ack,mouseAckStatus=ackStatus,mouseAckPixels=ackPixels,mouseAckUtc=ackAt.ToString("o"),metadataHooks=observed.MouseHook&&observed.KeyboardHook,monitorHealthy=observed.Healthy,monitorPollUtc=observed.PollUtc.ToString("o"),rawInputRegistered=observed.RawInput,mouseHookEvents=observed.MouseEvents,keyboardHookEvents=observed.KeyboardEvents,rawMouseEvents=observed.RawMouseEvents,rawKeyboardEvents=observed.RawKeyboardEvents,fallbackEvents=observed.FallbackEvents,ownMouseEvents=observed.OwnMouseEvents,monitorError=observed.Error});lastWrite=DateTime.UtcNow;
        }
    }
    static int Absolute(int value,int origin,int extent){return (int)Math.Max(0,Math.Min(65535,((2L*(value-origin)+1)*65536)/(2L*Math.Max(1,extent))));}
    protected override void ExitThreadCore(){timer.Stop();monitor.Dispose();timer.Dispose();base.ExitThreadCore();}
}
