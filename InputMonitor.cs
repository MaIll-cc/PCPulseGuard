using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

// Only timestamps, event categories and counters are retained. No key contents or
// pointer-position history leave the native input callback.
public sealed class InputClock {
    readonly object gate=new object();
    readonly Dictionary<uint,DateTime> ownTicks=new Dictionary<uint,DateTime>();
    DateTime lastUtc;string source="initial";uint seenTick;
    public InputClock(DateTime initial,uint tick){lastUtc=initial;seenTick=tick;}
    public void Own(uint tick,DateTime now){lock(gate){var expired=new List<uint>();foreach(var item in ownTicks)if((now-item.Value).TotalSeconds>10)expired.Add(item.Key);foreach(uint key in expired)ownTicks.Remove(key);ownTicks[tick]=now;}}
    public void Mark(DateTime at,string kind){lock(gate){if(at>lastUtc){lastUtc=at;source=kind;}}}
    public bool Poll(uint tick,uint nowTick,DateTime now){lock(gate){
        if(tick==seenTick)return false;seenTick=tick;
        DateTime own;if(ownTicks.TryGetValue(tick,out own)&&(now-own).TotalSeconds<=10)return false;
        DateTime at=now.AddMilliseconds(-unchecked(nowTick-tick));
        if(at<=lastUtc)return false;lastUtc=at;source="session_poll";return true;
    }}
    public void Read(out DateTime at,out string kind){lock(gate){at=lastUtc;kind=source;}}
}

public sealed class InputMonitorState {
    public DateTime LastUtc,PollUtc;public string Source,Error;
    public bool Healthy,MouseHook,KeyboardHook,RawInput;
    public long MouseEvents,KeyboardEvents,RawMouseEvents,RawKeyboardEvents,FallbackEvents,OwnMouseEvents;
}

// This message pump never reads or writes files. Keeping hooks off the activity
// publisher thread prevents a slow disk/antivirus scan from timing out a hook.
// Raw Input and unconditional session polling also cover silently removed hooks.
public sealed class InputMonitor:IDisposable {
    public const ulong OwnMarker=0x57535055;
    [StructLayout(LayoutKind.Sequential)]struct LastInput{public uint Size,Tick;}
    [DllImport("user32.dll")]static extern bool GetLastInputInfo(ref LastInput input);
    readonly InputClock clock;readonly Thread thread;readonly ManualResetEvent ready=new ManualResetEvent(false);
    readonly bool pollOnly;volatile MonitorWindow window;volatile string error="";
    volatile bool mouseHook,keyboardHook,rawInput;long pollTicks,mouseEvents,keyEvents,rawMouseEvents,rawKeyEvents,fallbackEvents,ownMouseEvents;
    public InputMonitor(bool pollingOnly){
        pollOnly=pollingOnly;var input=new LastInput{Size=8};DateTime now=DateTime.UtcNow;
        bool known=GetLastInputInfo(ref input);clock=new InputClock(known?now.AddMilliseconds(-unchecked((uint)Environment.TickCount-input.Tick)):now,input.Tick);
        thread=new Thread(Run){IsBackground=true,Name=AppInfo.Name+" input monitor"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
        ready.WaitOne(5000);
    }
    void Run(){try{using(var monitor=new MonitorWindow(this,pollOnly)){window=monitor;ready.Set();Application.Run();}}catch(Exception e){error=e.Message;ready.Set();}finally{window=null;mouseHook=keyboardHook=rawInput=false;}}
    void Poll(){var input=new LastInput{Size=8};if(GetLastInputInfo(ref input)){DateTime now=DateTime.UtcNow;if(clock.Poll(input.Tick,unchecked((uint)Environment.TickCount),now))Interlocked.Increment(ref fallbackEvents);Interlocked.Exchange(ref pollTicks,now.Ticks);}}
    public void Own(uint tick){clock.Own(tick,DateTime.UtcNow);}
    public InputMonitorState Read(){DateTime at;string kind;clock.Read(out at,out kind);long polled=Interlocked.Read(ref pollTicks);return new InputMonitorState{
        LastUtc=at,Source=kind,PollUtc=new DateTime(polled,DateTimeKind.Utc),Healthy=thread.IsAlive&&polled>0&&(DateTime.UtcNow-new DateTime(polled,DateTimeKind.Utc)).TotalSeconds<5,
        MouseHook=mouseHook,KeyboardHook=keyboardHook,RawInput=rawInput,Error=error,MouseEvents=Interlocked.Read(ref mouseEvents),KeyboardEvents=Interlocked.Read(ref keyEvents),RawMouseEvents=Interlocked.Read(ref rawMouseEvents),RawKeyboardEvents=Interlocked.Read(ref rawKeyEvents),FallbackEvents=Interlocked.Read(ref fallbackEvents),OwnMouseEvents=Interlocked.Read(ref ownMouseEvents)};}
    public void Dispose(){var target=window;if(target!=null)target.Stop();if(thread.IsAlive)thread.Join(3000);if(!thread.IsAlive)ready.Dispose();}

    sealed class MonitorWindow:NativeWindow,IDisposable {
        delegate IntPtr Hook(int code,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetWindowsHookEx(int id,Hook callback,IntPtr module,uint thread);
        [DllImport("user32.dll")]static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")]static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
        [StructLayout(LayoutKind.Sequential)]struct RawDevice{public ushort Page,Usage;public uint Flags;public IntPtr Target;}
        [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterRawInputDevices(RawDevice[] devices,uint count,uint size);
        [DllImport("user32.dll")]static extern uint GetRawInputData(IntPtr input,uint command,IntPtr data,ref uint size,uint header);
        readonly InputMonitor owner;readonly Hook mouseCallback,keyCallback;readonly System.Windows.Forms.Timer timer;
        readonly IntPtr rawBuffer;readonly uint headerSize=(uint)(8+IntPtr.Size*2);IntPtr mouse,key;bool disposed;
        internal MonitorWindow(InputMonitor monitor,bool pollingOnly){
            owner=monitor;CreateHandle(new CreateParams{Caption=AppInfo.Name+" input",Parent=new IntPtr(-3)});rawBuffer=Marshal.AllocHGlobal(128);
            mouseCallback=Mouse;keyCallback=Keyboard;
            if(!pollingOnly){
                var devices=new[]{new RawDevice{Page=1,Usage=2,Flags=0x100,Target=Handle},new RawDevice{Page=1,Usage=6,Flags=0x100,Target=Handle}};
                owner.rawInput=RegisterRawInputDevices(devices,2,(uint)Marshal.SizeOf(typeof(RawDevice)));
                mouse=SetWindowsHookEx(14,mouseCallback,GetModuleHandle(null),0);key=SetWindowsHookEx(13,keyCallback,GetModuleHandle(null),0);
                owner.mouseHook=mouse!=IntPtr.Zero;owner.keyboardHook=key!=IntPtr.Zero;
            }
            timer=new System.Windows.Forms.Timer{Interval=125};timer.Tick+=delegate{owner.Poll();};timer.Start();owner.Poll();
        }
        IntPtr Mouse(int code,IntPtr w,IntPtr data){if(code>=0){
            if(unchecked((ulong)Marshal.ReadInt64(data,24))==OwnMarker){owner.Own(unchecked((uint)Marshal.ReadInt32(data,16)));Interlocked.Increment(ref owner.ownMouseEvents);}
            else{owner.clock.Mark(DateTime.UtcNow,"mouse_hook");Interlocked.Increment(ref owner.mouseEvents);}
        }return CallNextHookEx(mouse,code,w,data);}
        IntPtr Keyboard(int code,IntPtr w,IntPtr data){if(code>=0&&unchecked((ulong)Marshal.ReadInt64(data,16))!=OwnMarker){owner.clock.Mark(DateTime.UtcNow,"keyboard_hook");Interlocked.Increment(ref owner.keyEvents);}return CallNextHookEx(key,code,w,data);}
        void Raw(IntPtr input){
            uint size=headerSize;if(GetRawInputData(input,0x10000005,rawBuffer,ref size,headerSize)!=headerSize)return;
            int type=Marshal.ReadInt32(rawBuffer);
            if(type==1){owner.clock.Mark(DateTime.UtcNow,"raw_keyboard");Interlocked.Increment(ref owner.rawKeyEvents);return;}
            if(type!=0)return;size=128;uint read=GetRawInputData(input,0x10000003,rawBuffer,ref size,headerSize);if(read==UInt32.MaxValue||read<headerSize+24)return;
            IntPtr mouseData=IntPtr.Add(rawBuffer,(int)headerSize);
            if(unchecked((uint)Marshal.ReadInt32(mouseData,20))==(uint)OwnMarker)return;
            if(Marshal.ReadInt16(mouseData,4)==0&&Marshal.ReadInt32(mouseData,12)==0&&Marshal.ReadInt32(mouseData,16)==0)return;
            owner.clock.Mark(DateTime.UtcNow,"raw_mouse");Interlocked.Increment(ref owner.rawMouseEvents);
        }
        protected override void WndProc(ref Message message){
            if(message.Msg==0xFF){try{Raw(message.LParam);}catch(Exception e){owner.error=e.Message;}}
            else if(message.Msg==0x8001){Application.ExitThread();return;}
            base.WndProc(ref message);
        }
        internal void Stop(){PostMessage(Handle,0x8001,IntPtr.Zero,IntPtr.Zero);}
        public void Dispose(){if(disposed)return;disposed=true;timer.Stop();timer.Dispose();if(mouse!=IntPtr.Zero)UnhookWindowsHookEx(mouse);if(key!=IntPtr.Zero)UnhookWindowsHookEx(key);
            if(owner.rawInput){RegisterRawInputDevices(new[]{new RawDevice{Page=1,Usage=2,Flags=1},new RawDevice{Page=1,Usage=6,Flags=1}},2,(uint)Marshal.SizeOf(typeof(RawDevice)));}
            Marshal.FreeHGlobal(rawBuffer);DestroyHandle();}
    }
}
