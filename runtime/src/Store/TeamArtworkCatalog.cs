using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using OfflineTeamBuilderRuntime;

namespace OfflineTeamBuilder.Repair;

// Presentation-only files. Does not read/write career data or install hooks.
internal sealed class TeamArtworkCatalog
{
    internal const int MaximumFileBytes=20*1024*1024, MaximumTotalBytes=192*1024*1024;
    private readonly UserRuntimeStore paths;
    private readonly Action<string> warn;
    private readonly object sync=new object();
    private readonly Dictionary<string,Picture> pictures=new Dictionary<string,Picture>(StringComparer.Ordinal);
    private readonly Dictionary<string,Picture> byFile=new Dictionary<string,Picture>(StringComparer.OrdinalIgnoreCase);
    private List<Team> teams=new List<Team>();
    private DateTime refreshAt;
    internal TeamArtworkCatalog(UserRuntimeStore paths,Action<string> warn=null){this.paths=paths;this.warn=warn;}
    private sealed class Settings {public string name=null;public string[] aliases=null;public int[] teamIds=null;}
    internal sealed class Team {
        internal string Name;internal string[] Aliases;internal int[] Ids;internal Picture Pcb,Tbak,Jersey,TsbLeft,TsbRight;
        internal Picture Get(string kind){switch(kind){case "pcb":return Pcb;case "tbak":return Tbak;case "jersey":return Jersey;case "tsb_left":return TsbLeft;case "tsb_right":return TsbRight;default:return null;}}
        internal IEnumerable<Picture> All()=>new[]{Pcb,Tbak,Jersey,TsbLeft,TsbRight}.Where(p=>p!=null);
    }
    internal sealed class Picture {internal byte[] Bytes;internal string Route,Hash;internal long Stamp,Length;internal int Width,Height;}
    internal List<Team> ForCompilation(){lock(sync){Refresh();return teams.ToList();}}
    internal string Root=>paths.Resolve("Artwork");
    internal bool TryReply(string path,int port,out byte[] bytes,out string contentType)
    {
        bytes=null;contentType=null;
        if(!path.StartsWith("/offline-tb/artwork/",StringComparison.Ordinal))return false;
        lock(sync){
            Refresh();
            if(path=="/offline-tb/artwork/manifest.xml"){bytes=Manifest(port);contentType="application/xml; charset=utf-8";return true;}
            Picture image;if(pictures.TryGetValue(path,out image)){bytes=image.Bytes;contentType="image/png";return true;}
            throw new ProtocolError(404,"No matching team artwork image.");
        }
    }
    private void Refresh()
    {
        if(DateTime.UtcNow<refreshAt)return;
        refreshAt=DateTime.UtcNow.AddSeconds(1);
        Directory.CreateDirectory(Root);
        var next=new List<Team>();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var ids=new HashSet<int>();
        long total=0;
        foreach(string folder in Directory.EnumerateDirectories(Root).OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).Take(65)){
            if(next.Count>=64){warn?.Invoke("Artwork folder limit reached (64).");break;}
            try{
                string label=Path.GetFileName(folder),relative="Artwork/"+label;
                paths.Resolve(relative); // Reject links/traversal before reading any file.
                var settings=new Settings();string config=paths.Resolve(relative+"/team.json");
                if(File.Exists(config)){
                    if(new FileInfo(config).Length>16384)throw new InvalidDataException("team.json exceeds 16 KB.");
                    settings=new JavaScriptSerializer().Deserialize<Settings>(File.ReadAllText(config))??new Settings();
                }
                string name=Normalize(settings.name??label);
                var aliases=new[]{name}.Concat(settings.aliases??new string[0]).Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var teamIds=(settings.teamIds??new int[0]).Distinct().ToArray();
                if(aliases.Length>16||teamIds.Length>32||teamIds.Any(id=>id<0||id>65534||id==255))throw new InvalidDataException("Invalid artwork team aliases/IDs.");
                if(aliases.Any(names.Contains)||teamIds.Any(ids.Contains))throw new InvalidDataException("Duplicate artwork name or team ID; ambiguous folder skipped.");
                var team=new Team{Name=name,Aliases=aliases,Ids=teamIds};
                team.Pcb=TryPicture(relative+"/pcb.png");team.Tbak=TryPicture(relative+"/tbak.png");
                team.Jersey=FirstPicture(relative,"jersey.png","teamjerseys.png","tjer.png");
                team.TsbLeft=TryPicture(relative+"/tsb_left.png");
                team.TsbRight=TryPicture(relative+"/tsb_right.png");
                long size=team.All().Sum(p=>(long)p.Bytes.Length);
                if(total+size>MaximumTotalBytes)throw new InvalidDataException("Artwork memory limit reached (192 MB).");
                if(!team.All().Any())continue;
                next.Add(team);total+=size;foreach(string alias in aliases)names.Add(alias);foreach(int id in teamIds)ids.Add(id);
            }catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is ArgumentException||ex is InvalidOperationException||ex is UnauthorizedAccessException){warn?.Invoke("Artwork folder "+Path.GetFileName(folder)+": "+ex.Message);}
        }
        teams=next;
        // Retain the previous catalog's hashes briefly so an in-flight native
        // image request survives a picture being replaced after its manifest.
        var active=next.SelectMany(t=>t.All()).Distinct().ToArray();
        var retired=pictures.Values.Distinct().Where(p=>!active.Contains(p)).Take(8).ToArray();
        pictures.Clear();long retained=total;
        foreach(var p in active)pictures[p.Route]=p;
        foreach(var p in retired)if(retained+p.Bytes.Length<=MaximumTotalBytes){pictures[p.Route]=p;retained+=p.Bytes.Length;}
        var liveFiles=new HashSet<Picture>(active);
        foreach(string key in byFile.Where(k=>!liveFiles.Contains(k.Value)).Select(k=>k.Key).ToArray())byFile.Remove(key);
    }
    private Picture TryPicture(string relative)
    {
        try{return ReadPicture(relative);}
        catch(Exception ex) when(ex is IOException||ex is InvalidDataException||ex is ArgumentException||ex is UnauthorizedAccessException||ex is System.Runtime.InteropServices.ExternalException){warn?.Invoke("Artwork "+relative+": "+ex.Message);return null;}
    }
    private Picture FirstPicture(string relative,params string[] names)
    {
        foreach(string name in names){string file=relative+"/"+name;if(File.Exists(paths.Resolve(file)))return TryPicture(file);}return null;
    }
    private Picture ReadPicture(string relative)
    {
        string file=paths.Resolve(relative);if(!File.Exists(file))return null;
        var info=new FileInfo(file);if(info.Length<45||info.Length>MaximumFileBytes)throw new InvalidDataException("PNG must be between 45 bytes and 20 MB.");
        Picture cached;if(byFile.TryGetValue(file,out cached)&&cached.Stamp==info.LastWriteTimeUtc.Ticks&&cached.Length==info.Length)return cached;
        byte[] bytes;
        using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read)){
            if(input.Length!=info.Length)throw new IOException("Artwork is being updated; retry after the copy finishes.");
            bytes=new byte[info.Length];int at=0;while(at<bytes.Length){int n=input.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}
        }
        byte[] signature={137,80,78,71,13,10,26,10};if(!bytes.Take(8).SequenceEqual(signature))throw new InvalidDataException("Artwork must be a PNG image.");
        uint width=Big32(bytes,16),height=Big32(bytes,20);
        if(width==0||height==0||width>8192||height>8192||(ulong)width*height>16777216)throw new InvalidDataException("PNG dimensions exceed the supported size.");
        using(var stream=new MemoryStream(bytes))using(var image=Image.FromStream(stream,false,true)){
            if(image.RawFormat.Guid!=ImageFormat.Png.Guid||image.Width!=width||image.Height!=height)throw new InvalidDataException("PNG could not be decoded consistently.");
        }
        string hash=UserRuntimeStore.Hash(bytes);
        var picture=new Picture{Bytes=bytes,Hash=hash,Route="/offline-tb/artwork/image/"+hash+".png",Stamp=info.LastWriteTimeUtc.Ticks,Length=info.Length,Width=(int)width,Height=(int)height};
        byFile[file]=picture;return picture;
    }
    private static uint Big32(byte[] b,int o)=>(uint)b[o]<<24|(uint)b[o+1]<<16|(uint)b[o+2]<<8|b[o+3];
    internal static string Normalize(string name){name=(name??"").Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant();if(name.Length==0||name.Length>128||name.Any(char.IsControl))throw new InvalidDataException("Invalid artwork team name.");return name;}
    private byte[] Manifest(int port)
    {
        using(var output=new MemoryStream()){
            using(var xml=XmlWriter.Create(output,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false,Indent=false})){
                xml.WriteStartElement("artwork");xml.WriteAttributeString("version","1");
                foreach(var t in teams)foreach(string alias in t.Aliases){
                    xml.WriteStartElement("team");xml.WriteAttributeString("name",alias);
                    xml.WriteAttributeString("ids",string.Join(",",t.Ids));
                    xml.WriteAttributeString("pcb",t.Pcb==null?"":"http://127.0.0.1:"+port.ToString(CultureInfo.InvariantCulture)+t.Pcb.Route);
                    xml.WriteAttributeString("tbak",t.Tbak==null?"":"http://127.0.0.1:"+port.ToString(CultureInfo.InvariantCulture)+t.Tbak.Route);
                    xml.WriteEndElement();
                }
                xml.WriteEndElement();
            }
            return output.ToArray();
        }
    }
}
