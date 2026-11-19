using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using OfflineTeamBuilder.Repair;

namespace OfflineTeamBuilderRuntime;

// Runs in a short-lived helper before Mod Manager applies mods. It never
// opens the game and never creates an object inside the game's UI interpreter.
internal static class ArtworkCompiler
{
    internal const string ModName="OfflineTeamBuilder.fbmod";
    internal const string Title="Offline TeamBuilder";
    internal sealed class Template {public int head;public string contentHash;public int maximumTeams;public Dictionary<string,Kind> kinds;public Dictionary<string,Group> groups;public Ui ui;public Ui[] extraUi;}
    internal sealed class Kind {public int libraryId,width,height,superbundle,selectorId;public string tableName,tableResRid,textureResMeta,group;public uint bundleHash,textureResType,tableResType,marker,bundleLookupIndex;public List<Tex> textures;public List<Table> tables;}
    internal sealed class Group {public int maximumPictures;public string tableName,tableResRid;public uint tableResType;public List<Table> tables;}
    internal sealed class Tex {public string name,fileGuid,instanceGuid,chunkGuid,rid,h64;public uint h32;public int id;}
    internal sealed class Table {public int count;public string resMeta;}
    internal sealed class Ui {public string file,asset,guid,h64;public uint h32;public int[] superbundles;public Collector[] collectors;public int[] names;public Number[] numbers;public string[] requiresAny;}
    internal sealed class Collector {public string name,guid;}
    internal sealed class Number {public uint marker;public int offset;}
    internal sealed class PictureRecord {internal Kind Kind;internal Tex Tex;internal TeamArtworkCatalog.Picture Picture;internal string Tag;}
    private sealed class Row {internal string Name;internal int Id=-1;internal Dictionary<string,int> Images;}
    private static byte[] Resource(string name){using(var s=typeof(ArtworkCompiler).Assembly.GetManifestResourceStream(name)){if(s==null)throw new IOException("Missing artwork compiler resource: "+name);using(var b=new MemoryStream()){s.CopyTo(b);return b.ToArray();}}}
    internal static void RequireResourceId(ulong rid){if((rid&1)==0||rid==ulong.MaxValue)throw new IOException("Invalid native artwork resource ID: serialized ResourceRefs require the ID tag (bit 0).");}
    internal static string Output(UserRuntimeStore paths)=>paths.Resolve("ArtworkCache/"+ModName);
    internal static object Compile(UserRuntimeStore paths)
    {
        byte[] templates=Resource("OfflineTeamBuilderRuntime.ArtworkTemplates.zip");
        var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);
        using(var z=new ZipArchive(new MemoryStream(templates),ZipArchiveMode.Read))foreach(var entry in z.Entries)using(var input=entry.Open())using(var b=new MemoryStream()){input.CopyTo(b);files.Add(entry.FullName,b.ToArray());}
        var json=new JavaScriptSerializer{MaxJsonLength=4*1024*1024};var template=json.Deserialize<Template>(Encoding.UTF8.GetString(files["manifest.json"]));
        // Reject malformed references before a cached mod can be reused or any
        // native asset is written. Even IDs can be mistaken for live pointers.
        var resourceIds=new HashSet<ulong>();
        foreach(var kind in template.kinds.Values){if(kind.group==null)RequireResourceId(ulong.Parse(kind.tableResRid,CultureInfo.InvariantCulture));foreach(var tex in kind.textures){ulong rid=ulong.Parse(tex.rid,CultureInfo.InvariantCulture);RequireResourceId(rid);if(!resourceIds.Add(rid))throw new IOException("Duplicate native artwork resource ID.");}}
        foreach(var group in template.groups.Values)RequireResourceId(ulong.Parse(group.tableResRid,CultureInfo.InvariantCulture));
        var warnings=new List<string>();var teams=new TeamArtworkCatalog(paths,warnings.Add).ForCompilation().Take(template.maximumTeams).ToArray();
        var pictures=new List<PictureRecord>();var rows=new List<Row>();var counts=template.kinds.Keys.ToDictionary(k=>k,k=>0);
        foreach(var team in teams){
            var row=new Row{Images=new Dictionary<string,int>()};
            foreach(string kind in template.kinds.Keys){
                var picture=team.Get(kind);if(picture==null)continue;
                var spec=template.kinds[kind];int index=counts[kind]++;var tex=spec.textures[index];
                pictures.Add(new PictureRecord{Kind=spec,Tex=tex,Picture=picture,Tag=kind+"-"+index.ToString("00")});
                row.Images[kind]=tex.id;
            }
            foreach(string alias in team.Aliases){if(Encoding.UTF8.GetByteCount(alias)>127){warnings.Add("Artwork alias omitted: exceeds 127 UTF-8 bytes.");continue;}if(rows.Count==32){warnings.Add("Additional artwork aliases omitted (32 selectors maximum).");break;}rows.Add(new Row{Name=alias,Images=row.Images});}
            foreach(int id in team.Ids){if(rows.Count==32)break;rows.Add(new Row{Name="__numeric_"+id,Id=id,Images=row.Images});}
        }
        byte[] baseline=Resource("OfflineTeamBuilderRuntime.BaseMod.fbmod");
        string fingerprint=UserRuntimeStore.Hash(Encoding.UTF8.GetBytes(PluginIdentity.Version+UserRuntimeStore.Hash(baseline)+UserRuntimeStore.Hash(templates)+ArtworkEncoder.Identity()+json.Serialize(rows.Select(r=>new{r.Name,r.Id,r.Images}).ToArray())+string.Join("|",pictures.Select(p=>p.Tag+":"+p.Picture.Hash))));
        string output=Output(paths),state=paths.Resolve("ArtworkCache/build.json");
        if(File.Exists(output)&&File.Exists(state)){
            try{var previous=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(state));if((string)previous["fingerprint"]==fingerprint&&(string)previous["modSha256"]==UserRuntimeStore.Hash(File.ReadAllBytes(output)))return new{ready=true,cached=true,output,fingerprint,teams=teams.Length,pictures=pictures.Count,warnings};}catch{}
        }
        var records=new List<byte[]>();var payloads=new List<byte[]>();
        foreach(string name in new[]{"Icon","Screenshot0","Screenshot1","Screenshot2","Screenshot3"})records.Add(Bytes(w=>{w.Write((byte)0);w.Write(-1);S(w,name);w.Write(0);}));
        void Native(byte type,string name,byte[] raw,bool added,int[] bundles,uint resType=0,ulong rid=0,byte[] resMeta=null){
            byte[] data=Cas(raw);int index=payloads.Count;payloads.Add(data);
            records.Add(Bytes(w=>{Base(w,type,index,name,data,raw.Length,added?8:0,0,"",bundles);if(type==2){w.Write(resType);w.Write(rid);w.Write(resMeta.Length);w.Write(resMeta);}}));
        }
        void Chunk(string guid,byte[] raw,int flags,int[] bundles,int[] superbundles,uint h32,ulong h64,string userData="",int handler=0){
            byte[] data=handler==0?Cas(raw):raw;int index=payloads.Count;payloads.Add(data);
            records.Add(Bytes(w=>{Base(w,3,index,guid,data,raw.Length,flags,handler,userData,bundles);w.Write(0u);w.Write(0u);w.Write(0u);w.Write(handler==0?(uint)raw.Length:0u);w.Write(h32);if(handler==0)w.Write(h64);w.Write(handler==0?-1:0);w.Write(superbundles.Length);foreach(int s in superbundles)w.Write(s);}));
        }
        foreach(var picture in pictures){
            var k=picture.Kind;var tex=picture.Tex;
            string cache=paths.Resolve("ArtworkCache/Textures/"+picture.Picture.Hash+"-"+k.width+"x"+k.height+"-"+ArtworkEncoder.Identity().Substring(0,16)+".bc7");
            int expected=k.width*k.height;
            byte[] bc7=null;if(File.Exists(cache)&&File.Exists(cache+".sha256")){var info=new FileInfo(cache);if(info.Length==expected){var candidate=File.ReadAllBytes(cache);if(UserRuntimeStore.Hash(candidate)==File.ReadAllText(cache+".sha256"))bc7=candidate;}}
            if(bc7==null){bc7=ArtworkEncoder.Encode(paths,picture.Picture.Bytes,k.width,k.height);if(bc7.Length!=expected)throw new IOException("Unexpected BC7 output size.");UserRuntimeStore.Atomic(cache,bc7);UserRuntimeStore.Atomic(cache+".sha256",Encoding.ASCII.GetBytes(UserRuntimeStore.Hash(bc7)));}
            Native(1,tex.name,files[picture.Tag+".ebx"],true,new[]{unchecked((int)k.bundleHash)});
            Native(2,tex.name,files[picture.Tag+".res"],true,new[]{unchecked((int)k.bundleHash)},k.textureResType,ulong.Parse(tex.rid,CultureInfo.InvariantCulture),Hex(k.textureResMeta));
            Chunk(tex.chunkGuid,bc7,8,new[]{unchecked((int)k.bundleHash)},new[]{k.superbundle},tex.h32,ulong.Parse(tex.h64,CultureInfo.InvariantCulture));
        }
        foreach(string kind in new[]{"pcb","tbak"}){
            int count=counts[kind];if(count==0)continue;var k=template.kinds[kind];string prefix=kind+"-table-"+count.ToString("00");
            Native(1,k.tableName,files[prefix+".ebx"],false,new int[0]);
            Native(2,k.tableName,files[prefix+".res"],false,new int[0],k.tableResType,ulong.Parse(k.tableResRid,CultureInfo.InvariantCulture),Hex(k.tables[count].resMeta));
        }
        foreach(var entry in template.groups){
            string groupName=entry.Key;var group=entry.Value;var selected=pictures.Where(p=>p.Kind.group==groupName).ToArray();if(selected.Length==0)continue;
            byte[] ebx=ArtworkLibraryWriter.PatchMetadata(files[groupName+"-menu-table-"+selected.Length.ToString("00")+".ebx"],groupName,selected);
            byte[] brt=ArtworkLibraryWriter.BuildResource(files[groupName+"-source.res"],selected,out byte[] resMeta);
            Native(1,group.tableName,ebx,false,new int[0]);Native(2,group.tableName,brt,false,new int[0],group.tableResType,ulong.Parse(group.tableResRid,CultureInfo.InvariantCulture),resMeta);
        }
        var collectorRows=new Dictionary<string,List<byte[]>>(StringComparer.OrdinalIgnoreCase);
        var collectorNames=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var u in new[]{template.ui}.Concat(template.extraUi??new Ui[0])) {
        if(u.requiresAny!=null && !u.requiresAny.Any(kind=>counts[kind]>0))continue;
        byte[] ui=(byte[])files[u.file??"ui.ast"].Clone();
        for(int i=0;i<u.names.Length;i++){
            byte[] name=Encoding.UTF8.GetBytes(i<rows.Count?rows[i].Name:"__unused_artwork_"+i);if(name.Length>127)throw new IOException("Artwork name exceeds the native selector's UTF-8 limit.");
            int at=u.names[i];if(at<0||at+129>ui.Length)throw new IOException("Artwork name template offset invalid.");Array.Clear(ui,at,128);Buffer.BlockCopy(name,0,ui,at,name.Length);
        }
        foreach(var patch in u.numbers){
            if(patch.offset<1||patch.offset+4>ui.Length||BitConverter.ToUInt32(ui,patch.offset)!=patch.marker)throw new IOException("Artwork selector template changed.");
            uint value;int slot=(int)(patch.marker&255);
            if(patch.marker==0x53FFFF00)value=(uint)rows.Count;
            else if(patch.marker==0x54FFFF00)value=(uint)counts["jersey"];
            else if(patch.marker==0x51FFFF00)value=(uint)counts["pcb"];
            else if((patch.marker&0xFF000000)==0x53000000)value=slot<rows.Count&&rows[slot].Id>=0?(uint)rows[slot].Id:0x7FFFFFFFu;
            else {uint prefix=patch.marker&0xFF000000;string key=prefix==0x51000000?"pcb":prefix==0x52000000?"tbak":template.kinds.First(k=>k.Value.marker==prefix).Key;int id;value=(uint)(slot<rows.Count&&rows[slot].Images.TryGetValue(key,out id)?id:0);}
            Buffer.BlockCopy(BitConverter.GetBytes(value),0,ui,patch.offset,4);
        }
        Chunk(u.guid,ui,8,new int[0],u.superbundles,u.h32,ulong.Parse(u.h64,CultureInfo.InvariantCulture),"legacy;"+u.asset);
        byte[] uiCas=payloads.Last();
        foreach(var collector in u.collectors){
            List<byte[]> entries;
            if(!collectorRows.TryGetValue(collector.guid,out entries)){entries=new List<byte[]>();collectorRows.Add(collector.guid,entries);collectorNames.Add(collector.guid,collector.name);}
            else if(collectorNames[collector.guid]!=collector.name)throw new IOException("Artwork collector identity mismatch.");
            byte[] row=Bytes(w=>{w.Write(u.h32);w.Write(new Guid(u.guid).ToByteArray());w.Write(0L);w.Write(0L);w.Write((long)uiCas.Length);w.Write((long)ui.Length);});
            if(entries.Any(e=>BitConverter.ToUInt32(e,0)==u.h32))throw new IOException("Duplicate artwork UI replacement.");
            entries.Add(row);
        }
        }
        foreach(var collector in collectorRows)Chunk(collector.Key,collector.Value.SelectMany(row=>row).ToArray(),0,new int[0],new int[0],0,0,"legacy;Collector ("+collectorNames[collector.Key]+")",-1113851035);
        byte[] mod=UnifiedModWriter.Merge(baseline,Pack(template,records,payloads,PluginIdentity.Version+"-"+fingerprint.Substring(0,12)));
        UserRuntimeStore.Atomic(output,mod);
        var report=new{ready=true,cached=false,output,fingerprint,modSha256=UserRuntimeStore.Hash(mod),teams=teams.Length,pictures=pictures.Count,selectors=rows.Count,pcbCount=counts["pcb"],tbakCount=counts["tbak"],counts,warnings,nativeImageLibraries=true,xmlUsed=false,gameProcessOpened=false};
        UserRuntimeStore.Atomic(state,Encoding.UTF8.GetBytes(json.Serialize(report)));return report;
    }
    private static byte[] Pack(Template t,List<byte[]> records,List<byte[]> payloads,string version){
        using(var output=new MemoryStream())using(var w=new BinaryWriter(output)){
            w.Write(72155812747760198UL);w.Write(9);w.Write(0L);w.Write(payloads.Count);byte[] profile=Encoding.UTF8.GetBytes("CollegeFB27");w.Write((byte)profile.Length);w.Write(profile);w.Write(t.head);w.Write(Hex(t.contentHash));
            foreach(string s in new[]{Title,PluginIdentity.Authors,"TeamBuilder",version,"Use downloaded TeamBuilder teams in offline Dynasty with uniforms, stadiums, logos and stadium sounds. Add optional per-school PNGs for hub backgrounds, player-card backgrounds and portrait jerseys. Requires Offline TeamBuilder Runtime. Restart MMC and the game after changing artwork.",""})S(w,s);
            w.Write(records.Count);foreach(var r in records)w.Write(r);long manifest=output.Position,offset=0;foreach(var p in payloads){w.Write(offset);w.Write((long)p.Length);offset+=p.Length;}foreach(var p in payloads)w.Write(p);output.Position=12;w.Write(manifest);return output.ToArray();
        }
    }
    private static void Base(BinaryWriter w,byte type,int index,string name,byte[] payload,int size,int flags,int handler,string userData,int[] bundles){w.Write(type);w.Write(index);S(w,name);using(var sha=SHA1.Create())w.Write(sha.ComputeHash(payload));w.Write((long)size);w.Write((byte)flags);w.Write(handler);S(w,userData);w.Write(bundles.Length);foreach(int b in bundles)w.Write(b);}
    private static byte[] Bytes(Action<BinaryWriter> write){using(var s=new MemoryStream())using(var w=new BinaryWriter(s)){write(w);return s.ToArray();}}
    private static void S(BinaryWriter w,string s){w.Write(Encoding.UTF8.GetBytes(s));w.Write((byte)0);}
    private static byte[] Hex(string s){if(s==null||s.Length%2!=0)throw new IOException("Invalid artwork template hex.");return Enumerable.Range(0,s.Length/2).Select(i=>byte.Parse(s.Substring(i*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture)).ToArray();}
    private static byte[] Cas(byte[] bytes)=>Bytes(w=>{for(int at=0;at<bytes.Length;at+=32768){int n=Math.Min(32768,bytes.Length-at);w.Write(new[]{(byte)(n>>24),(byte)(n>>16),(byte)(n>>8),(byte)n,(byte)0,(byte)0,(byte)(n>>8),(byte)n});w.Write(bytes,at,n);}});
}

internal static class ArtworkEncoder
{
    private static IntPtr library;private static EncodeDelegate encode;private static byte[] payload;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EncodeDelegate(byte[] pixels,uint width,uint height,byte[] output,uint bytes);
    private static byte[] Payload(){if(payload==null)using(var s=typeof(ArtworkEncoder).Assembly.GetManifestResourceStream("OfflineTeamBuilderRuntime.ArtworkCodec.dll")){if(s==null)throw new IOException("Artwork encoder payload missing.");using(var b=new MemoryStream()){s.CopyTo(b);payload=b.ToArray();}}return payload;}
    internal static string Identity()=>UserRuntimeStore.Hash(Payload());
    internal static byte[] Encode(UserRuntimeStore paths,byte[] png,int width,int height)
    {
        if(!((width==1920&&height==1080)||(width==3840&&height==2160)||(width==512&&height==512)||(width==1024&&height==1024)||(width==840&&height==1080)))throw new IOException("Unexpected native artwork dimensions.");
        if(encode==null){
            byte[] data=Payload();string path=paths.Resolve("ArtworkCache/Codec/"+Identity()+"/OfflineTeamBuilder.ArtworkCodec.dll");
            if(!File.Exists(path))UserRuntimeStore.PublishNew(path,data);
            if(UserRuntimeStore.Hash(File.ReadAllBytes(path))!=Identity())throw new IOException("Artwork codec integrity check failed.");
            library=LoadLibrary(path);if(library==IntPtr.Zero)throw new IOException("Cannot load artwork codec: "+Marshal.GetLastWin32Error());
            IntPtr entry=GetProcAddress(library,"OTBA_EncodeBGRA");if(entry==IntPtr.Zero)throw new IOException("Artwork codec export missing.");encode=(EncodeDelegate)Marshal.GetDelegateForFunctionPointer(entry,typeof(EncodeDelegate));
        }
        byte[] pixels=new byte[width*height*4];
        using(var stream=new MemoryStream(png))using(var input=Image.FromStream(stream,false,true))using(var resized=new Bitmap(width,height,PixelFormat.Format32bppArgb)){
            using(var graphics=Graphics.FromImage(resized)){graphics.CompositingMode=CompositingMode.SourceCopy;graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;graphics.DrawImage(input,new Rectangle(0,0,width,height));}
            var bits=resized.LockBits(new Rectangle(0,0,width,height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try{if(bits.Stride!=width*4)throw new IOException("Unexpected artwork pixel stride.");Marshal.Copy(bits.Scan0,pixels,0,pixels.Length);}finally{resized.UnlockBits(bits);}
        }
        byte[] output=new byte[width*height];int result=encode(pixels,(uint)width,(uint)height,output,(uint)output.Length);if(result!=0)throw new IOException("Artwork codec failed: "+result);return output;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr LoadLibrary(string path);
    [DllImport("kernel32.dll",CharSet=CharSet.Ansi,SetLastError=true)]private static extern IntPtr GetProcAddress(IntPtr module,string name);
}
