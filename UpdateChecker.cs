using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public sealed class UpdateResult {public string Code,Version,Url;public bool Available;}
public static class UpdateChecker {
    public static Version ParseVersion(string tag){if(tag==null||!Regex.IsMatch(tag,@"^v?\d{1,6}\.\d{1,6}\.\d{1,6}(\.\d{1,6})?$"))return null;Version v;return Version.TryParse(tag.TrimStart('v'),out v)?new Version(v.Major,v.Minor,Math.Max(0,v.Build),Math.Max(0,v.Revision)):null;}
    public static bool ReleaseUrlAllowed(string url){Uri uri;return Uri.TryCreate(url,UriKind.Absolute,out uri)&&uri.Scheme=="https"&&uri.Host=="github.com"&&uri.IsDefaultPort&&String.IsNullOrEmpty(uri.UserInfo)&&uri.AbsolutePath.StartsWith("/"+AppInfo.Repository+"/releases/tag/",StringComparison.Ordinal)&&String.IsNullOrEmpty(uri.Query)&&String.IsNullOrEmpty(uri.Fragment);}
    public static UpdateResult Parse(Dictionary<string,object> data){if(PulseIO.Flag(data,"draft")||PulseIO.Flag(data,"prerelease"))return new UpdateResult{Code="no_release"};string tag=PulseIO.Text(data,"tag_name"),url=PulseIO.Text(data,"html_url");Version next=ParseVersion(tag),current=ParseVersion(AppInfo.Version);if(next==null||!ReleaseUrlAllowed(url))return new UpdateResult{Code="invalid"};bool available=next>current;return new UpdateResult{Code=available?"available":"current",Available=available,Version=tag,Url=url};}
    public static Task<UpdateResult> CheckAsync(){return Task.Run(delegate{try{
        ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
        var request=(HttpWebRequest)WebRequest.Create(AppInfo.LatestReleaseApi);request.UserAgent=AppInfo.Name+"/"+AppInfo.Version;request.Accept="application/vnd.github+json";request.Headers["X-GitHub-Api-Version"]="2022-11-28";request.Timeout=10000;request.ReadWriteTimeout=10000;request.AllowAutoRedirect=false;
        using(var response=(HttpWebResponse)request.GetResponse())using(var stream=response.GetResponseStream())using(var output=new MemoryStream()){
            byte[] buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){if(output.Length+count>262144)return new UpdateResult{Code="invalid"};output.Write(buffer,0,count);}string text=System.Text.Encoding.UTF8.GetString(output.ToArray());return Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string,object>>(text));
        }
    }catch(WebException e){var response=e.Response as HttpWebResponse;try{return new UpdateResult{Code=response!=null&&response.StatusCode==HttpStatusCode.NotFound?"no_release":response!=null&&(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)?"rate_limit":"network"};}finally{if(response!=null)response.Dispose();}}catch{return new UpdateResult{Code="network"};}});}
}
