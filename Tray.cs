using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

sealed class CleanNumber:NumericUpDown {
    protected override void UpdateEditText(){Text=Value.ToString(DecimalPlaces>0?"0.####":"0");}
}

sealed class TimeBox:UserControl {
    readonly CleanNumber value;readonly ComboBox unit;readonly double maximum;
    bool loading;string previous="s";
    internal Action Changed;
    internal string Unit {get{return unit.SelectedIndex==2?"h":unit.SelectedIndex==1?"min":"s";}}
    internal double Amount {get{return (double)value.Value;}}
    internal TimeBox(string name,double max) {
        maximum=max;Size=new Size(260,30);AutoScaleMode=AutoScaleMode.None;
        value=new CleanNumber{DecimalPlaces=4,Minimum=0.0001m,Maximum=(decimal)max,Value=1,Bounds=new Rectangle(0,0,148,28),AccessibleName=L.T(name)+" / "+L.T("数值")};
        unit=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(160,0,96,28),AccessibleName=L.T(name)+" / "+L.T("单位")};unit.Items.AddRange(new object[]{L.T("秒"),L.T("分钟"),L.T("小时")});unit.SelectedIndex=0;
        Controls.Add(value);Controls.Add(unit);
        value.ValueChanged+=delegate{if(!loading&&Changed!=null)Changed();};value.Validated+=delegate{if(!loading&&Changed!=null)Changed();};
        unit.SelectedIndexChanged+=delegate {
            if(loading)return;double seconds=(double)value.Value*PulseSettings.Factor(previous);previous=Unit;
            loading=true;value.Maximum=(decimal)(maximum/PulseSettings.Factor(Unit));value.Value=Math.Max(value.Minimum,Math.Min(value.Maximum,(decimal)(seconds/PulseSettings.Factor(Unit))));loading=false;
            if(Changed!=null)Changed();
        };
    }
    internal void Set(double amount,string code) {loading=true;unit.SelectedIndex=code=="h"?2:code=="min"?1:0;previous=Unit;value.Maximum=(decimal)(maximum/PulseSettings.Factor(Unit));value.Value=Math.Max(value.Minimum,Math.Min(value.Maximum,(decimal)amount));loading=false;}
}

static class PulseIcon {
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr h);
    internal static Icon Make(Color color) {
        using(var image=new Bitmap(64,64))using(var g=Graphics.FromImage(image)) {
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var b=new SolidBrush(color))g.FillEllipse(b,2,2,60,60);
            using(var p=new Pen(Color.White,5)){p.StartCap=p.EndCap=LineCap.Round;p.LineJoin=LineJoin.Round;g.DrawLines(p,new PointF[]{new PointF(12,33),new PointF(22,33),new PointF(28,19),new PointF(36,46),new PointF(42,30),new PointF(52,30)});}
            IntPtr h=image.GetHicon();try{using(var icon=Icon.FromHandle(h))return (Icon)icon.Clone();}finally{DestroyIcon(h);}
        }
    }
    internal static void Scale(Form f,int width,int height) {float scale;using(var g=f.CreateGraphics())scale=g.DpiX/96f;f.SuspendLayout();if(Math.Abs(scale-1)>0.01)f.Scale(new SizeF(scale,scale));f.ClientSize=new Size((int)(width*scale),(int)(height*scale));f.ResumeLayout(true);}
}

sealed class LiveView {
    internal string Code,Title,Detail,Countdown,Tooltip,Health,Last,Cpu,Gpu,Idle,Updated,Uptime;
    internal Color Color;internal bool Healthy;internal double Remaining;
    internal Dictionary<string,object> Raw;
    static int previousPid;static DateTime checkedAt;static bool alive;
    static bool IsAlive(int pid){if(pid!=previousPid||(DateTime.UtcNow-checkedAt).TotalSeconds>=5){previousPid=pid;checkedAt=DateTime.UtcNow;try{using(var p=Process.GetProcessById(pid))alive=p.ProcessName==Path.GetFileNameWithoutExtension(AppInfo.CoreFile);}catch{alive=false;}}return alive;}
    internal static LiveView Read(string root,bool verifyPid) {
        var d=PulseIO.Read(Path.Combine(root,"pulse-status.json"));var last=PulseIO.Read(Path.Combine(root,"pulse-last.json"));var s=PulseSettings.Load(root);
        DateTime now=DateTime.UtcNow,sample=PulseIO.Time(d,"time");double age=(now-sample).TotalSeconds;
        var v=new LiveView{Raw=d,Code=PulseIO.Text(d,"code"),Title=PulseIO.Text(d,"reason"),Detail="",Remaining=PulseIO.Number(d,"remainingSeconds",-1),Color=Color.FromArgb(25,143,111)};
        v.Healthy=age>=-3&&age<6&&PulseIO.Text(d,"version")==AppInfo.Version&&(!verifyPid||IsAlive((int)PulseIO.Number(d,"pid",0)));
        v.Cpu=PulseIO.Flag(d,"cpuKnown")?PulseIO.Number(d,"cpuPercent",0).ToString("0.0")+"%":L.T("采样中");
        v.Gpu=PulseIO.Flag(d,"gpuKnown")?PulseIO.Number(d,"gpuPercent",0).ToString("0.0")+"%":L.T("暂不可用");
        v.Idle=PulseIO.Flag(d,"idleKnown")?PulseIO.Duration(PulseIO.Number(d,"humanIdleSeconds",0)+Math.Max(0,age)):L.T("等待活动信息");
        v.Updated=sample==DateTime.MinValue?L.T("等待后台采样"):sample.ToLocalTime().ToString("HH:mm:ss")+" · "+L.F("{0} 秒前",Math.Max(0,(int)age));
        var heart=PulseIO.Read(Path.Combine(root,"heartbeat.json"));v.Uptime=PulseIO.Duration(PulseIO.Number(heart,"uptimeSeconds",0));
        v.Health=v.Healthy?"后台正常运行":"后台未连接或数据已过期";
        string result=PulseIO.Text(last,"status"),at=PulseIO.Time(last,"time")==DateTime.MinValue?"":PulseIO.Time(last,"time").ToLocalTime().ToString("MM-dd HH:mm:ss");
        v.Last=result=="completed"?at+" · "+L.F("已完成（{0} 秒）",PulseIO.Number(last,"durationSeconds",0).ToString("0.0")):result=="partial"?at+" · "+L.T("部分动作不可用，详见日志"):result=="cancelled"?at+" · "+L.T("用户活动、设置变化或负载保护，已结束"):result=="error"?at+" · "+L.T("执行异常，详见日志"):L.T("尚无心跳记录");
        if(v.Remaining>=0)v.Remaining=Math.Max(0,v.Remaining-Math.Max(0,age));
        v.Detail="以无人操作时间为首要条件；内存和显存中的驻留内容不计为忙碌。";
        if(v.Code=="disabled"){v.Color=Color.FromArgb(112,123,137);v.Detail="设置仍可修改，重新勾选即可恢复心跳。";}
        else if(v.Code=="busy"){v.Color=Color.FromArgb(49,114,188);v.Detail="等待计算负载下降后复查，不会重新累计一整段无人操作时间。";}
        else if(v.Code=="pulsing"){v.Color=Color.FromArgb(125,87,208);v.Detail="恢复操作或关闭保活时会结束负载；自己的鼠标动作不会重置无人操作计时。";}
        else if(v.Code=="no_actions"||v.Code=="activity_unknown"||v.Code=="temperature"){v.Color=Color.FromArgb(177,117,26);v.Detail="当前不执行心跳，请查看设置或运行状态。";}
        if(v.Healthy&&(result=="error"||result=="partial")&&v.Code!="pulsing"){v.Color=Color.FromArgb(177,117,26);v.Health="后台运行中 · 上次心跳有异常";}
        if(!s.Enabled){v.Code="disabled";v.Title="心跳保活已关闭";v.Color=Color.FromArgb(112,123,137);v.Remaining=-1;}
        else if(!v.Healthy){v.Code="offline";v.Title="正在连接后台";v.Detail="若持续未连接，可查看日志或重新打开应用。";v.Color=Color.FromArgb(207,63,77);v.Remaining=-1;}
        v.Countdown=v.Remaining<0?"--:--":PulseIO.Duration(v.Remaining);
        v.Title=L.State(v.Code);v.Detail=L.T(v.Detail);v.Health=L.T(v.Health);
        string next=v.Code=="pulsing"?L.F("本次剩余 {0}",v.Countdown):v.Remaining>=0?L.F("预计还有 {0}",v.Countdown):L.T("双击查看详情");
        // Put the countdown first so it survives the legacy tray tooltip's 63-character limit.
        v.Tooltip=next+"\n"+v.Title+"\n"+AppInfo.Name;if(v.Tooltip.Length>63)v.Tooltip=v.Tooltip.Substring(0,63);return v;
    }
}

sealed class PulseWindow:Form {
    internal Action<PulseSettings> SaveRequested;internal Action DefaultsRequested,LogsRequested,CheckUpdatesRequested;internal Action<string> LanguageRequested;
    readonly TabControl tabs;readonly FlowLayoutPanel settingsFlow;
    readonly CheckBox enabled,mouse,cpu,gpu,skip,wake,display,memory,disk;
    readonly TimeBox idleTime,repeatTime,durationTime;readonly NumericUpDown cpuTarget,gpuTarget,cpuBusy,gpuBusy,mousePixels;
    readonly Label state,detail,countdown,saved,health,cpuValue,gpuValue,idleValue,updated,last,uptime;
    readonly Panel stateCard;readonly RichTextBox logText;
    readonly ComboBox languageBox;readonly Label updateStatus;readonly Button checkUpdates,downloadUpdate;readonly CheckBox autoUpdates;readonly GroupBox updateSection;
    readonly CheckBox startup,silentStartup;readonly Button applyStartup;readonly Label startupStatus,startupHint;bool startupLoading,startupBusy;
    internal Action<bool,bool> StartupRequested;
    string releaseUrl="";
    internal int SelectedTab {get{return tabs.SelectedIndex;}set{tabs.SelectedIndex=Math.Max(0,Math.Min(2,value));}}
    readonly System.Windows.Forms.Timer debounce;bool loading;string appliedRevision="";int logTicks;
    static readonly Color Ink=Color.FromArgb(29,43,61),Muted=Color.FromArgb(103,117,134);
    Label TextAt(Control parent,string text,int x,int y,int w,int h,float size) {var l=new Label{Text=text,Bounds=new Rectangle(x,y,w,h),Font=new Font("Microsoft YaHei UI",size),ForeColor=Ink,AutoEllipsis=true};parent.Controls.Add(l);return l;}
    CheckBox CheckAt(Control parent,string text,int x,int y,int w) {var c=new CheckBox{Text=text,Bounds=new Rectangle(x,y,w,29),Font=new Font("Microsoft YaHei UI",9),AccessibleName=text};parent.Controls.Add(c);c.CheckedChanged+=delegate{Edited();};return c;}
    NumericUpDown PercentAt(Control parent,string name,int x,int y) {var n=new NumericUpDown{Minimum=1,Maximum=100,Value=5,Bounds=new Rectangle(x,y,80,28),AccessibleName=name,Font=new Font("Microsoft YaHei UI",9)};parent.Controls.Add(n);TextAt(parent,"%",x+88,y+3,28,25,9);n.ValueChanged+=delegate{Edited();};n.Validated+=delegate{Edited();};return n;}
    GroupBox Group(FlowLayoutPanel flow,string name,int height) {var g=new GroupBox{Text=name,Width=680,Height=height,Margin=new Padding(0,0,0,12),BackColor=Color.White,Font=new Font("Microsoft YaHei UI",9,FontStyle.Bold)};flow.Controls.Add(g);return g;}
    TimeBox TimeAt(Control parent,string name,int y,double max) {TextAt(parent,name,18,y+3,230,26,9);var box=new TimeBox(name,max){Location=new Point(290,y)};parent.Controls.Add(box);box.Changed=Edited;return box;}
    internal PulseWindow() {
        Text=AppInfo.Name;Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(244,247,250);ForeColor=Ink;AutoScaleMode=AutoScaleMode.None;StartPosition=FormStartPosition.CenterScreen;ClientSize=new Size(744,824);Icon=PulseIcon.Make(Color.FromArgb(25,143,111));
        var header=new Panel{Dock=DockStyle.Top,Height=76,BackColor=Color.FromArgb(25,41,61)};Controls.Add(header);
        var picture=new PictureBox{Bounds=new Rectangle(23,16,44,44),Image=Icon.ToBitmap(),SizeMode=PictureBoxSizeMode.Zoom};header.Controls.Add(picture);
        var title=TextAt(header,AppInfo.Name,82,12,490,32,17);title.ForeColor=Color.White;title.Font=new Font("Segoe UI",17,FontStyle.Bold);TextAt(header,"v"+AppInfo.Version+" · CPU / GPU / Mouse",84,48,440,21,8).ForeColor=Color.LightSteelBlue;
        languageBox=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Bounds=new Rectangle(594,27,131,27),AccessibleName=L.T("语言"),Font=new Font("Microsoft YaHei UI",9)};languageBox.Items.AddRange(L.Names);languageBox.SelectedIndex=Array.IndexOf(L.Codes,L.Code);header.Controls.Add(languageBox);languageBox.SelectedIndexChanged+=delegate{if(loading||LanguageRequested==null)return;Commit();LanguageRequested(L.Codes[languageBox.SelectedIndex]);};
        var bottom=new Panel{Dock=DockStyle.Bottom,Height=52,Padding=new Padding(18,9,18,9)};Controls.Add(bottom);
        saved=TextAt(bottom,"设置会自动保存",18,16,400,25,8);saved.ForeColor=Muted;
        var hide=new Button{Text="隐藏到托盘",Width=130,Dock=DockStyle.Right,FlatStyle=FlatStyle.Flat,BackColor=Color.White};hide.Click+=delegate{Hide();};bottom.Controls.Add(hide);
        tabs=new TabControl{Dock=DockStyle.Fill,Padding=new Point(16,8)};Controls.Add(tabs);tabs.BringToFront();
        var mainTab=new TabPage("保活设置"){BackColor=BackColor};var statusTab=new TabPage("运行状态"){BackColor=BackColor};var advancedTab=new TabPage("更多选项"){BackColor=BackColor};tabs.TabPages.AddRange(new TabPage[]{mainTab,statusTab,advancedTab});
        settingsFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18,12,18,12)};mainTab.Controls.Add(settingsFlow);
        var enablePanel=new Panel{Width=680,Height=38,Margin=new Padding(0,0,0,8)};settingsFlow.Controls.Add(enablePanel);enabled=CheckAt(enablePanel,"开启心跳保活",0,0,440);enabled.Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold);
        stateCard=new Panel{Width=680,Height=91,BackColor=Color.White,Margin=new Padding(0,0,0,12)};settingsFlow.Controls.Add(stateCard);
        state=TextAt(stateCard,"正在读取后台状态",15,11,450,28,12);state.Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold);detail=TextAt(stateCard,"",15,45,470,38,8);detail.ForeColor=Muted;
        countdown=TextAt(stateCard,"--:--",505,20,160,44,23);countdown.Font=new Font("Microsoft YaHei UI",23,FontStyle.Bold);
        var trigger=Group(settingsFlow,"触发时间",116);idleTime=TimeAt(trigger,"无人操作多久后触发心跳",27,604800);repeatTime=TimeAt(trigger,"两次心跳的最短间隔",73,604800);
        var actions=Group(settingsFlow,"心跳动作（可以多选）",211);
        mouse=CheckAt(actions,"移动鼠标",18,25,240);
        mousePixels=new NumericUpDown{Minimum=1,Maximum=10000,Value=1,Bounds=new Rectangle(290,26,80,28),AccessibleName="鼠标移动像素数",Font=new Font("Microsoft YaHei UI",9)};actions.Controls.Add(mousePixels);mousePixels.ValueChanged+=delegate{Edited();};mousePixels.Validated+=delegate{Edited();};
        TextAt(actions,"像素",378,30,48,24,9);TextAt(actions,"随后移回原位，不点击",438,30,220,24,8).ForeColor=Muted;
        cpu=CheckAt(actions,"CPU 负载",18,62,240);cpuTarget=PercentAt(actions,"CPU 目标负载百分比",290,63);TextAt(actions,"目标负载",418,67,180,24,8).ForeColor=Muted;
        gpu=CheckAt(actions,"GPU 负载",18,99,240);gpuTarget=PercentAt(actions,"GPU 目标负载百分比",290,100);TextAt(actions,"目标负载",418,104,180,24,8).ForeColor=Muted;
        durationTime=TimeAt(actions,"负载持续时间",141,86400);
        TextAt(actions,"按目标比例间歇运算；实际利用率会受硬件和现有任务影响。",18,180,640,24,8).ForeColor=Muted;
        var guard=Group(settingsFlow,"忙碌时避让",107);skip=CheckAt(guard,"高负载时跳过心跳",18,23,560);
        TextAt(guard,"CPU 超过",18,64,110,25,9);cpuBusy=PercentAt(guard,"CPU 忙碌阈值百分比",126,61);TextAt(guard,"或 GPU 超过",340,64,125,25,9);gpuBusy=PercentAt(guard,"GPU 忙碌阈值百分比",474,61);
        var tip=new Label{Text="只监测用户输入和整机计算负载。模型或缓存驻留内存、显存不视为忙碌。",Width=680,Height=37,ForeColor=Muted,Font=new Font("Microsoft YaHei UI",8),Margin=new Padding(0,0,0,8)};settingsFlow.Controls.Add(tip);
        var statusFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18)};statusTab.Controls.Add(statusFlow);
        var readings=Group(statusFlow,"实时状态",222);
        TextAt(readings,"后台",18,29,130,24,9);health=TextAt(readings,"等待连接",160,29,480,24,9);
        TextAt(readings,"无人操作",18,65,130,24,9);idleValue=TextAt(readings,"--",160,65,480,24,9);
        TextAt(readings,"CPU 实际利用率",18,101,170,24,9);cpuValue=TextAt(readings,"--",220,101,430,24,11);
        TextAt(readings,"GPU 实际利用率",18,137,170,24,9);gpuValue=TextAt(readings,"--",220,137,430,24,11);
        updated=TextAt(readings,"",18,177,630,22,8);updated.ForeColor=Muted;
        var recent=Group(statusFlow,"最近一次心跳",106);last=TextAt(recent,"尚无记录",18,28,640,36,9);uptime=TextAt(recent,"",18,72,640,24,8);uptime.ForeColor=Muted;
        var logs=Group(statusFlow,"运行日志",302);logText=new RichTextBox{ReadOnly=true,BorderStyle=BorderStyle.None,BackColor=Color.White,Bounds=new Rectangle(14,27,650,226),Font=new Font("Consolas",8),WordWrap=false,AccessibleName="运行日志"};logs.Controls.Add(logText);var openLog=new Button{Text="打开完整日志",Bounds=new Rectangle(18,262,150,28)};logs.Controls.Add(openLog);openLog.Click+=delegate{if(LogsRequested!=null)LogsRequested();};
        var advancedFlow=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18)};advancedTab.Controls.Add(advancedFlow);
        var startupGroup=Group(advancedFlow,"启动设置",214);
        startup=new CheckBox{Text="开机自启动",Bounds=new Rectangle(18,25,630,29),AccessibleName="开机自启动"};startupGroup.Controls.Add(startup);
        silentStartup=new CheckBox{Text="静默启动（仅显示托盘图标）",Bounds=new Rectangle(42,59,606,29),AccessibleName="静默启动（仅显示托盘图标）"};startupGroup.Controls.Add(silentStartup);
        startupHint=TextAt(startupGroup,"",18,94,640,44,8);startupHint.ForeColor=Muted;
        applyStartup=new Button{Text="应用启动设置",Bounds=new Rectangle(18,151,225,31)};startupGroup.Controls.Add(applyStartup);
        startupStatus=TextAt(startupGroup,"",260,151,390,43,8);
        startup.CheckedChanged+=delegate{if(!startupLoading){silentStartup.Enabled=startup.Checked&&!startupBusy;startupStatus.Text=L.T("启动设置尚未应用");}};
        silentStartup.CheckedChanged+=delegate{if(!startupLoading)startupStatus.Text=L.T("启动设置尚未应用");};
        applyStartup.Click+=delegate{if(StartupRequested!=null)StartupRequested(startup.Checked,silentStartup.Checked);};
        var power=Group(advancedFlow,"系统唤醒",116);wake=CheckAt(power,"开启保活时，阻止电脑自动睡眠",18,27,630);display=CheckAt(power,"同时保持屏幕点亮",18,70,630);
        var extras=Group(advancedFlow,"其他心跳动作（可选）",147);memory=CheckAt(extras,"触碰并检查少量内存（64 MiB）",18,28,630);disk=CheckAt(extras,"写入临时文件（4 MiB，完成后自动删除）",18,67,630);TextAt(extras,"这些动作同样遵守无人操作时间和高负载避让设置。",18,110,630,24,8).ForeColor=Muted;
        var app=Group(advancedFlow,"应用",174);TextAt(app,"关闭窗口会隐藏到托盘；退出图标后，独立后台仍继续工作。",18,27,630,44,9);TextAt(app,"GPU 计算使用 Direct3D 11；不可用时会记录原因，其他动作可继续。",18,75,630,46,8).ForeColor=Muted;
        var reset=new Button{Text="恢复默认设置",Bounds=new Rectangle(18,129,160,29)};app.Controls.Add(reset);reset.Click+=delegate{if(DefaultsRequested!=null)DefaultsRequested();};
        var updates=Group(advancedFlow,"版本与更新",235);updateSection=updates;TextAt(updates,L.F("当前版本：{0}",AppInfo.Version),18,26,610,24,9);
        checkUpdates=new Button{Text=L.T("检查版本更新"),Bounds=new Rectangle(18,60,225,31)};updates.Controls.Add(checkUpdates);checkUpdates.Click+=delegate{if(CheckUpdatesRequested!=null)CheckUpdatesRequested();};
        var repository=new LinkLabel{Text=L.T("打开 GitHub 项目"),Bounds=new Rectangle(270,66,360,27),AccessibleName=L.T("打开 GitHub 项目")};updates.Controls.Add(repository);repository.LinkClicked+=delegate{OpenLink(AppInfo.RepositoryUrl);};
        autoUpdates=new CheckBox{Text=L.T("自动检查更新（每天）"),Bounds=new Rectangle(18,100,620,29),Checked=UiPreferences.Load(PulseIO.Root).AutoCheckUpdates};updates.Controls.Add(autoUpdates);autoUpdates.CheckedChanged+=delegate{var pref=UiPreferences.Load(PulseIO.Root);pref.AutoCheckUpdates=autoUpdates.Checked;pref.Save(PulseIO.Root);};
        updateStatus=TextAt(updates,"尚未检查更新",18,137,630,27,9);downloadUpdate=new Button{Text=L.T("查看新版与下载"),Bounds=new Rectangle(18,170,275,30),Visible=false};updates.Controls.Add(downloadUpdate);downloadUpdate.Click+=delegate{if(UpdateChecker.ReleaseUrlAllowed(releaseUrl))OpenLink(releaseUrl);};
        TextAt(updates,"仅检查 GitHub Releases；不会自动下载安装。",18,208,630,23,8).ForeColor=Muted;
        debounce=new System.Windows.Forms.Timer{Interval=650};debounce.Tick+=delegate{debounce.Stop();Commit();};
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Commit();Hide();}};
        settingsFlow.Resize+=delegate{Fit(settingsFlow);};statusFlow.Resize+=delegate{Fit(statusFlow);};advancedFlow.Resize+=delegate{Fit(advancedFlow);};
        PulseIcon.Scale(this,744,824);
        Shown+=delegate{var area=Screen.FromControl(this).WorkingArea;if(Height>area.Height-24)Height=area.Height-24;CenterToScreen();Fit(settingsFlow);Fit(statusFlow);Fit(advancedFlow);};
        L.Apply(this);LoadSettings(PulseSettings.Load(PulseIO.Root));SetStartup(StartupManager.Read(PulseIO.Root));
    }
    internal void SetStartup(StartupState value){startupLoading=true;startup.Checked=value.Enabled;silentStartup.Checked=value.Silent;startupLoading=false;startupHint.Text=L.T(value.Installed?"安装版：后台随系统启动；界面在登录后启动。修改需要管理员授权。":"便携版：当前用户登录 Windows 后启动。移动程序目录后请重新设置。");startupStatus.Text=L.T(!value.Known?"无法读取启动状态，请查看日志":value.Mixed?"部分启动任务已启用，请重新应用":value.Enabled?"自启动已开启":"自启动已关闭");SetStartupBusy(false);}
    internal void SetStartupBusy(bool busy){startupBusy=busy;startup.Enabled=silentStartup.Enabled=applyStartup.Enabled=languageBox.Enabled=!busy;silentStartup.Enabled=!busy&&startup.Checked;if(busy)startupStatus.Text=L.T("正在应用启动设置…");}
    internal void SetStartupResult(string code){if(code!="ok")startupStatus.Text=L.T(code=="cancelled"?"授权已取消，启动设置未修改":"启动设置失败，请查看日志");}
    static void OpenLink(string url){Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}
    internal void SetUpdate(UpdateResult result,bool checking){checkUpdates.Enabled=!checking;downloadUpdate.Visible=false;if(checking){updateStatus.Text=L.T("正在检查版本…");return;}if(result==null){updateStatus.Text=L.T("尚未检查更新");return;}releaseUrl=result.Url;downloadUpdate.Visible=result.Available;updateStatus.Text=result.Code=="available"?L.F("发现新版本：{0}",result.Version):L.T(result.Code=="current"?"已是最新稳定版本":result.Code=="no_release"?"尚无公开的稳定版本":result.Code=="invalid"?"版本信息无法验证，请到 GitHub 查看":result.Code=="rate_limit"?"GitHub 请求过于频繁，请稍后重试":"网络连接失败，请稍后重试");}
    internal void ShowUpdates(){ShowSettings();tabs.SelectedIndex=2;((ScrollableControl)updateSection.Parent).ScrollControlIntoView(updateSection);}
    static void Fit(FlowLayoutPanel flow){int width=Math.Max(550,flow.ClientSize.Width-flow.Padding.Horizontal-SystemInformation.VerticalScrollBarWidth-3);foreach(Control c in flow.Controls)c.Width=width;}
    void Edited(){if(loading||debounce==null)return;mousePixels.Enabled=mouse.Checked;cpuTarget.Enabled=cpu.Checked;gpuTarget.Enabled=gpu.Checked;cpuBusy.Enabled=gpuBusy.Enabled=skip.Checked;saved.Text=L.T("正在保存设置…");debounce.Stop();if(!enabled.Checked)Commit();else debounce.Start();}
    internal PulseSettings Current() {var s=new PulseSettings{Enabled=enabled.Checked,IdleValue=idleTime.Amount,IdleUnit=idleTime.Unit,RepeatValue=repeatTime.Amount,RepeatUnit=repeatTime.Unit,DurationValue=durationTime.Amount,DurationUnit=durationTime.Unit,MouseEnabled=mouse.Checked,MousePixels=(int)mousePixels.Value,CpuEnabled=cpu.Checked,GpuEnabled=gpu.Checked,CpuPercent=(int)cpuTarget.Value,GpuPercent=(int)gpuTarget.Value,SkipHighLoad=skip.Checked,CpuBusyPercent=(int)cpuBusy.Value,GpuBusyPercent=(int)gpuBusy.Value,KeepSystemAwake=wake.Checked,KeepDisplayAwake=display.Checked,MemoryEnabled=memory.Checked,DiskEnabled=disk.Checked};s.Normalize();return s;}
    void Commit(){if(loading||SaveRequested==null)return;try{var s=Current();SaveRequested(s);appliedRevision=s.Revision;saved.Text=L.T("已保存 · 后台约 1 秒内生效");}catch(Exception e){saved.Text=L.T("保存失败：").TrimEnd()+" "+e.Message;}}
    internal void LoadSettings(PulseSettings s){loading=true;enabled.Checked=s.Enabled;idleTime.Set(s.IdleValue,s.IdleUnit);repeatTime.Set(s.RepeatValue,s.RepeatUnit);durationTime.Set(s.DurationValue,s.DurationUnit);mouse.Checked=s.MouseEnabled;mousePixels.Value=s.MousePixels;mousePixels.Enabled=s.MouseEnabled;cpu.Checked=s.CpuEnabled;gpu.Checked=s.GpuEnabled;skip.Checked=s.SkipHighLoad;cpuTarget.Value=s.CpuPercent;gpuTarget.Value=s.GpuPercent;cpuBusy.Value=s.CpuBusyPercent;gpuBusy.Value=s.GpuBusyPercent;wake.Checked=s.KeepSystemAwake;display.Checked=s.KeepDisplayAwake;memory.Checked=s.MemoryEnabled;disk.Checked=s.DiskEnabled;cpuTarget.Enabled=s.CpuEnabled;gpuTarget.Enabled=s.GpuEnabled;cpuBusy.Enabled=gpuBusy.Enabled=s.SkipHighLoad;appliedRevision=s.Revision;loading=false;}
    internal void UpdateView(LiveView v){state.Text=v.Title;state.ForeColor=countdown.ForeColor=v.Color;detail.Text=v.Detail;countdown.Text=v.Countdown;health.Text=v.Health;health.ForeColor=v.Color;cpuValue.Text=v.Cpu;gpuValue.Text=v.Gpu;idleValue.Text=v.Idle;updated.Text=L.F("负载更新：{0}",v.Updated);last.Text=v.Last;uptime.Text=L.F("系统运行：{0}",v.Uptime);if(v.Healthy&&PulseIO.Text(v.Raw,"settingsRevision")==appliedRevision&&!debounce.Enabled)saved.Text=L.T("设置已生效 · 自动保存");if(tabs.SelectedIndex==1&&logTicks++%5==0)ReadLog();}
    void ReadLog(){try{string path=Path.Combine(PulseIO.Root,"watchdog.log");using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(f.Length>24000)f.Seek(-24000,SeekOrigin.End);using(var r=new StreamReader(f))logText.Text=r.ReadToEnd();}logText.SelectionStart=logText.TextLength;logText.ScrollToCaret();}catch{logText.Text=L.T("暂无日志。");}}
    internal void ShowSettings(){tabs.SelectedIndex=0;Show();if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Activate();}
    protected override void Dispose(bool disposing){if(disposing&&debounce!=null)debounce.Dispose();base.Dispose(disposing);}
}

sealed class PulseTray:ApplicationContext {
    PulseWindow window;readonly NotifyIcon icon;ContextMenuStrip menu;readonly System.Windows.Forms.Timer timer;
    readonly EventWaitHandle show,quit;ToolStripMenuItem master,status,updateItem;Icon current;Color color=Color.Empty;int ticks;bool checking;UpdateResult updateResult;
    internal PulseTray(EventWaitHandle open,EventWaitHandle exit,bool visible) {
        show=open;quit=exit;CreateWindow();
        current=PulseIcon.Make(Color.FromArgb(25,143,111));icon=new NotifyIcon{Icon=current,Text=AppInfo.Name,Visible=true};icon.DoubleClick+=delegate{window.ShowSettings();};BuildMenu();
        timer=new System.Windows.Forms.Timer{Interval=1000};timer.Tick+=delegate{try{if(quit.WaitOne(0)){ExitThread();return;}if(show.WaitOne(0))window.ShowSettings();Refresh();if(ticks>10&&ticks%30==0){var pref=UiPreferences.Load(PulseIO.Root);DateTime lastCheck;if(pref.AutoCheckUpdates&&(!DateTime.TryParse(pref.LastUpdateCheckUtc,out lastCheck)||(DateTime.UtcNow-lastCheck.ToUniversalTime()).TotalHours>=24))CheckForUpdates();}}catch(Exception e){PulseIO.Log(Path.Combine(PulseIO.Root,"activity"),"UI: "+e.Message);}};timer.Start();Refresh();if(visible){window.ShowSettings();EnsureInstalledEngine();}
    }
    void CreateWindow(){window=new PulseWindow();window.SaveRequested=Save;window.LogsRequested=OpenLogs;window.DefaultsRequested=delegate{var s=new PulseSettings();s.Save(PulseIO.Root);window.LoadSettings(s);};window.CheckUpdatesRequested=CheckForUpdates;window.StartupRequested=ApplyStartup;window.LanguageRequested=delegate(string code){window.BeginInvoke((Action)delegate{var old=window;bool visible=old.Visible;Point location=old.Location;int tab=old.SelectedTab;var pref=UiPreferences.Load(PulseIO.Root);pref.Language=code;pref.Save(PulseIO.Root);L.Set(code);CreateWindow();BuildMenu();window.SetUpdate(updateResult,checking);if(visible){window.ShowSettings();window.SelectedTab=tab;window.Location=location;}old.Dispose();Refresh();});};}
    async void ApplyStartup(bool enabled,bool silent){window.SetStartupBusy(true);string result=await StartupManager.ApplyAsync(PulseIO.Root,enabled,silent,false);if(window.IsDisposed)return;window.SetStartup(StartupManager.Read(PulseIO.Root));window.SetStartupResult(result);}
    async void EnsureInstalledEngine(){if(!StartupManager.Installed(PulseIO.Root))return;await System.Threading.Tasks.Task.Delay(2000);if(window.IsDisposed||LiveView.Read(PulseIO.Root,true).Healthy)return;window.SetStartupBusy(true);string result=await StartupManager.ApplyAsync(PulseIO.Root,false,true,true);if(!window.IsDisposed){window.SetStartup(StartupManager.Read(PulseIO.Root));window.SetStartupResult(result);}}
    void BuildMenu(){var old=menu;menu=new ContextMenuStrip{Font=new Font("Microsoft YaHei UI",9)};menu.Items.Add(L.T("打开保活设置"),null,delegate{window.ShowSettings();});
        master=new ToolStripMenuItem(L.T("开启心跳保活"));master.Click+=delegate{var s=PulseSettings.Load(PulseIO.Root);s.Enabled=!s.Enabled;Save(s);window.LoadSettings(s);};menu.Items.Add(master);status=new ToolStripMenuItem(L.T("读取状态中")){Enabled=false};menu.Items.Add(status);menu.Items.Add(new ToolStripSeparator());
        var idleMenu=new ToolStripMenuItem(L.T("无人操作多久后触发"));foreach(int n in new int[]{1,5,10,15,30,60}){int minutes=n;idleMenu.DropDownItems.Add(n+" "+L.T("分钟"),null,delegate{var s=PulseSettings.Load(PulseIO.Root);s.IdleValue=minutes;s.IdleUnit="min";Save(s);window.LoadSettings(s);});}menu.Items.Add(idleMenu);
        menu.Items.Add(L.T("全部时间与动作设置…"),null,delegate{window.ShowSettings();});updateItem=new ToolStripMenuItem(L.T("检查版本更新"));updateItem.Click+=delegate{window.ShowUpdates();CheckForUpdates();};menu.Items.Add(updateItem);menu.Items.Add(L.T("查看完整日志"),null,delegate{OpenLogs();});menu.Items.Add(L.T("使用说明"),null,delegate{OpenFile(Path.Combine(PulseIO.Root,"README.txt"));});menu.Items.Add(new ToolStripSeparator());menu.Items.Add(L.T("退出图标（后台继续）"),null,delegate{ExitThread();});menu.Opening+=delegate{master.Checked=PulseSettings.Load(PulseIO.Root).Enabled;};window.ContextMenuStrip=menu;icon.ContextMenuStrip=menu;if(old!=null)old.Dispose();}
    async void CheckForUpdates(){if(checking)return;checking=true;window.SetUpdate(updateResult,true);try{var pref=UiPreferences.Load(PulseIO.Root);pref.LastUpdateCheckUtc=DateTime.UtcNow.ToString("o");pref.Save(PulseIO.Root);updateResult=await UpdateChecker.CheckAsync();if(!window.IsDisposed)window.SetUpdate(updateResult,false);updateItem.Text=updateResult.Available?L.F("发现新版本：{0}",updateResult.Version):L.T("检查版本更新");}catch{updateResult=new UpdateResult{Code="network"};if(!window.IsDisposed)window.SetUpdate(updateResult,false);}finally{checking=false;}}
    void Save(PulseSettings s){s.Save(PulseIO.Root);Refresh();}
    void Refresh(){var v=LiveView.Read(PulseIO.Root,true);icon.Text=v.Tooltip;status.Text=v.Title;if(color!=v.Color){Icon old=current;current=PulseIcon.Make(v.Color);icon.Icon=current;color=v.Color;old.Dispose();}if(window.Visible)window.UpdateView(v);if(ticks++%5==0)PulseIO.Save(Path.Combine(PulseIO.Root,"activity","tray-status-"+Process.GetCurrentProcess().SessionId+".json"),new {time=DateTimeOffset.Now.ToString("o"),pid=Process.GetCurrentProcess().Id,code=v.Code,tooltip=v.Tooltip,healthy=v.Healthy,windowVisible=window.Visible});}
    static void OpenFile(string path){Process.Start(new ProcessStartInfo("notepad.exe","\""+path+"\""){UseShellExecute=true});}
    static void OpenLogs(){OpenFile(Path.Combine(PulseIO.Root,"watchdog.log"));}
    protected override void ExitThreadCore(){timer.Stop();icon.Visible=false;icon.Dispose();window.Dispose();menu.Dispose();current.Dispose();timer.Dispose();base.ExitThreadCore();}
}

static class TrayProgram {
    [DllImport("user32.dll")]static extern bool SetProcessDPIAware();
    static void StartPortableCompanions() {
        if(PulseIO.Flag(PulseIO.Read(Path.Combine(PulseIO.Root,"installation.json")),"machine"))return;
        string exe=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,AppInfo.CoreFile);if(!File.Exists(exe))return;
        string quoted=" --root \""+PulseIO.Root+"\"";
        Process.Start(new ProcessStartInfo(exe,"--activity"+quoted){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=PulseIO.Root});
        Process.Start(new ProcessStartInfo(exe,quoted){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=PulseIO.Root});
    }
    [STAThread]static int Main(string[] args) {
        PulseIO.SetRoot(args);L.Set(UiPreferences.Load(PulseIO.Root).Language);string key=PulseIO.Key(PulseIO.Root),openName=@"Local\PCPulseGuardShow-"+key,quitName=@"Local\PCPulseGuardQuit-"+key;
        try {
            if(Array.IndexOf(args,"--quit")>=0){try{using(var q=EventWaitHandle.OpenExisting(quitName))q.Set();}catch{}return 0;}
            bool created;using(var single=new Mutex(true,@"Local\PCPulseGuardTray-"+key,out created))using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,openName))using(var quit=new EventWaitHandle(false,EventResetMode.AutoReset,quitName)) {
                if(!created){if(Array.IndexOf(args,"--tray")<0)show.Set();return 0;}
                SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Directory.CreateDirectory(Path.Combine(PulseIO.Root,"activity"));StartPortableCompanions();
                Application.ThreadException+=delegate(object sender,ThreadExceptionEventArgs e){PulseIO.Log(Path.Combine(PulseIO.Root,"activity"),"UI failed: "+e.Exception.Message);Environment.Exit(1);};
                Application.Run(new PulseTray(show,quit,Array.IndexOf(args,"--tray")<0));return 0;
            }
        }catch(Exception e){PulseIO.Log(Path.Combine(PulseIO.Root,"activity"),"UI fatal: "+e.Message);return 1;}
    }
}
