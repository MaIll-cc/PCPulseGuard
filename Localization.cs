using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

public sealed class UiPreferences {
    public string Language=""; public bool AutoCheckUpdates=true,SilentStartup=true;public string LastUpdateCheckUtc="";
    public static UiPreferences Load(string root){try{var p=PulseIO.Json.Deserialize<UiPreferences>(PulseIO.ReadText(Path.Combine(root,"activity","ui-preferences.json"),8192));p.Language=L.Normalize(p.Language);return p;}catch{return new UiPreferences{Language=L.Normalize("")};}}
    public void Save(string root){Language=L.Normalize(Language);PulseIO.Save(Path.Combine(root,"activity","ui-preferences.json"),this);}
}

public static class L {
    public static readonly string[] Codes={"zh-CN","en","zh-TW","ja","de","es","fr"};
    public static readonly string[] Names={"简体中文","English","繁體中文","日本語","Deutsch","Español","Français"};
    static readonly Dictionary<string,string[]> catalog=Read();static int language;
    public static string Code {get{return Codes[language];}}
    public static string Normalize(string code){foreach(string c in Codes)if(String.Equals(c,code,StringComparison.OrdinalIgnoreCase))return c;if(String.IsNullOrEmpty(code)){string system=CultureInfo.CurrentUICulture.Name;if(system.StartsWith("zh",StringComparison.OrdinalIgnoreCase))return system=="zh-TW"||system=="zh-HK"||system=="zh-Hant"?"zh-TW":"zh-CN";foreach(string c in Codes)if(system.StartsWith(c,StringComparison.OrdinalIgnoreCase))return c;}return "en";}
    public static void Set(string code){language=Array.IndexOf(Codes,Normalize(code));}
    public static string T(string key){if(key==null)return "";string[] row;return catalog.TryGetValue(key,out row)?row[language]:key;}
    public static string F(string key,params object[] args){return String.Format(CultureInfo.CurrentCulture,T(key),args);}
    public static int Validate(){int count=0;foreach(var pair in catalog){if(pair.Value.Length!=Codes.Length)throw new Exception("Invalid language count: "+pair.Key);foreach(string value in pair.Value){if(String.IsNullOrWhiteSpace(value))throw new Exception("Missing translation: "+pair.Key);for(int i=0;i<4;i++)if(pair.Key.Contains("{"+i+"}")!=value.Contains("{"+i+"}"))throw new Exception("Translation placeholder mismatch: "+pair.Key);}count++;}return count;}
    static Dictionary<string,string[]> Read(){var result=new Dictionary<string,string[]>(StringComparer.Ordinal);using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Languages.tsv"))using(var reader=new StreamReader(stream)){string line;while((line=reader.ReadLine())!=null){if(line.Length==0||line.StartsWith("#"))continue;string[] cells=line.Split('|');if(cells.Length!=7)throw new Exception("Invalid translation row: "+line);result.Add(cells[0],cells);}}return result;}
    public static void Apply(Control control){if(!(control is TextBoxBase)&&!(control is NumericUpDown))control.Text=T(control.Text);if(!String.IsNullOrEmpty(control.AccessibleName))control.AccessibleName=T(control.AccessibleName);foreach(Control child in control.Controls)Apply(child);}
    public static string State(string code){switch(code){case "disabled":return T("心跳保活已关闭");case "idle_wait":return T("等待无人操作达到设定时间");case "cooldown":return T("等待下一次心跳");case "busy":return T("计算负载超过设定阈值，正在避让");case "sampling":return T("正在建立 CPU 负载采样");case "ready":return T("已满足条件，准备执行心跳");case "pulsing":return T("正在执行心跳");case "no_actions":return T("请选择至少一种心跳动作");case "activity_unknown":return T("等待可靠的用户活动信息");case "temperature":return T("显卡温度保护，暂不执行心跳");default:return T("正在连接后台");}}
}
