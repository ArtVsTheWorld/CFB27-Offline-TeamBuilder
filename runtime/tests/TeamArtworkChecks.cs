using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Xml;
using OfflineTeamBuilder.Repair;
using OfflineTeamBuilderRuntime;

internal static class TeamArtworkChecks
{
    private static void Check(bool value,string name)=>Program.Assert(value,"Artwork: "+name);
    private static void Picture(string path,Color color){Directory.CreateDirectory(Path.GetDirectoryName(path));using(var bitmap=new Bitmap(64,36)){using(var g=Graphics.FromImage(bitmap))g.Clear(color);bitmap.Save(path,ImageFormat.Png);}}
    private static XmlDocument Manifest(TeamArtworkCatalog catalog){byte[] bytes;string type;Check(catalog.TryReply("/offline-tb/artwork/manifest.xml",27875,out bytes,out type),"manifest handled");Check(type.StartsWith("application/xml"),"manifest content type");var doc=new XmlDocument{XmlResolver=null};doc.LoadXml(Encoding.UTF8.GetString(bytes));return doc;}
    internal static void CheckAll()
    {
        string root=Path.Combine(Path.GetTempPath(),"otb-artwork-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try{
            var paths=new UserRuntimeStore(root);var warnings=new System.Collections.Generic.List<string>();
            var empty=new TeamArtworkCatalog(paths,warnings.Add);Check(Manifest(empty).SelectNodes("/artwork/team").Count==0,"empty folder preserves native artwork");
            string east=Path.Combine(root,"Artwork","East Point"),famu=Path.Combine(root,"Artwork","Florida A&M");
            Picture(Path.Combine(east,"tbak.png"),Color.Green);Picture(Path.Combine(east,"pcb.png"),Color.DarkGreen);Picture(Path.Combine(famu,"tbak.png"),Color.Orange);
            File.WriteAllText(Path.Combine(east,"team.json"),"{\"aliases\":[\"EAST POINT GREEN STORM\"],\"teamIds\":[101]}");
            var catalog=new TeamArtworkCatalog(paths,warnings.Add);var xml=Manifest(catalog);
            Check(xml.SelectNodes("/artwork/team").Count==3,"multiple folders and aliases");
            var row=xml.SelectSingleNode("/artwork/team[@name='east point']");Check(row!=null&&row.Attributes["ids"].Value=="101","optional explicit team binding");
            string tbak=row.Attributes["tbak"].Value,pcb=row.Attributes["pcb"].Value;
            Check(tbak!=pcb&&tbak.StartsWith("http://127.0.0.1:27875/offline-tb/artwork/image/"),"separate PCB/TBAK content-addressed URLs");
            var famuRow=xml.SelectSingleNode("/artwork/team[@name='florida a&m']");Check(famuRow!=null&&famuRow.Attributes["pcb"].Value=="","missing individual kind omitted");
            byte[] bytes;string type;Check(catalog.TryReply(new Uri(tbak).AbsolutePath,27875,out bytes,out type)&&bytes.SequenceEqual(File.ReadAllBytes(Path.Combine(east,"tbak.png")))&&type=="image/png","exact PNG served");
            Check(!catalog.TryReply("/health",27875,out bytes,out type),"native routes not intercepted");
            Program.Reject(()=>catalog.TryReply("/offline-tb/artwork/image/../../secret",27875,out bytes,out type),"unknown/traversal artwork route rejected");
            Picture(Path.Combine(east,"tbak.png"),Color.Blue);Thread.Sleep(1100);xml=Manifest(catalog);
            string replaced=xml.SelectSingleNode("/artwork/team[@name='east point']").Attributes["tbak"].Value;Check(replaced!=tbak,"changed picture gets a new native cache key");
            Check(catalog.TryReply(new Uri(tbak).AbsolutePath,27875,out bytes,out type),"in-flight previous image retained");
            File.WriteAllBytes(Path.Combine(east,"pcb.png"),new byte[80]);Thread.Sleep(1100);xml=Manifest(catalog);row=xml.SelectSingleNode("/artwork/team[@name='east point']");
            Check(row.Attributes["pcb"].Value==""&&row.Attributes["tbak"].Value==replaced,"bad PCB cannot disable valid TBAK");
            Check(warnings.Count!=0,"invalid picture has a diagnostic");
            Check(TeamArtworkCatalog.Normalize("  EAST POINT  ")=="east point","friendly folder matching");
            Program.Reject(()=>TeamArtworkCatalog.Normalize("bad\nname"),"control-character alias rejected");
            Directory.CreateDirectory(Path.Combine(root,"Artwork","Invalid"));File.WriteAllText(Path.Combine(root,"Artwork","Invalid","team.json"),"{\"teamIds\":[-1]}");
            Check(Manifest(new TeamArtworkCatalog(paths,warnings.Add)).SelectNodes("/artwork/team[@name='invalid']").Count==0,"invalid config is isolated");
            // Exercise real HTTP routing without a game or the installed store.
            var options=new RepairOptions{StoreDirectory=Path.Combine(root,"PrivateStore")};
            using(var server=new LocalMcrService(Path.Combine(root,"Session"),options,0)){
                server.Start();
                int status;
                try{using(var response=(HttpWebResponse)WebRequest.Create("http://127.0.0.1:"+server.Port+"/offline-tb/artwork/image/missing.png").GetResponse())status=(int)response.StatusCode;}catch(WebException ex){using(var response=(HttpWebResponse)ex.Response)status=(int)response.StatusCode;}
                Check(status==404,"unknown image returns HTTP 404");
                Check(server.GuardFailure==""&&server.IsHealthy,"artwork failure does not poison native transport guards");
                using(var response=(HttpWebResponse)WebRequest.Create("http://127.0.0.1:"+server.Port+"/offline-tb/artwork/manifest.xml").GetResponse())Check(response.StatusCode==HttpStatusCode.OK&&response.ContentType.StartsWith("application/xml"),"real HTTP XML response");
            }
        }finally{
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("otb-artwork-",StringComparison.Ordinal)&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
