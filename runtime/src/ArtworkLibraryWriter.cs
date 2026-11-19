using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OfflineTeamBuilderRuntime;

// Fixed native-layout templates keep this helper independent of the editor SDK.
// Existing library metadata and BRT lookups are retained byte-for-byte.
internal static class ArtworkLibraryWriter
{
    private sealed class Lookup {internal ulong Hash;internal uint Bundle,Name;}
    internal static byte[] PatchMetadata(byte[] template,string group,ArtworkCompiler.PictureRecord[] pictures)
    {
        byte[] output=(byte[])template.Clone();
        for(int i=0;i<pictures.Length;i++){
            byte[] token=Encoding.ASCII.GetBytes(("__OTBA_"+group+"_"+i.ToString("00")+"__").PadRight(128,'~'));
            int at=Unique(output,token);byte[] name=Encoding.UTF8.GetBytes(pictures[i].Tex.name);
            if(name.Length>127)throw new IOException("Native artwork asset path exceeds the template limit.");
            Array.Clear(output,at,128);Buffer.BlockCopy(name,0,output,at,name.Length);
            at=Unique(output,BitConverter.GetBytes(0x6F3A7100+i));Buffer.BlockCopy(BitConverter.GetBytes(pictures[i].Tex.id),0,output,at,4);
        }
        return output;
    }
    private static int Unique(byte[] data,byte[] needle)
    {
        int found=-1;
        for(int at=0;at<=data.Length-needle.Length;at++){
            int j=0;while(j<needle.Length&&data[at+j]==needle[j])j++;if(j!=needle.Length)continue;
            if(found>=0)throw new IOException("Ambiguous native artwork template marker.");found=at;
        }
        if(found<0)throw new IOException("Native artwork template marker is missing.");return found;
    }
    internal static byte[] BuildResource(byte[] source,ArtworkCompiler.PictureRecord[] pictures,out byte[] resMeta)
    {
        if(source.Length<100)throw new IOException("Native artwork BRT header is truncated.");
        int strings=checked((int)BitConverter.ToUInt64(source,16)),bundles=checked((int)BitConverter.ToUInt64(source,24)),lookups=checked((int)BitConverter.ToUInt64(source,8));
        int count=checked((int)BitConverter.ToUInt32(source,72));uint bundleCount=BitConverter.ToUInt32(source,76);
        if(strings<80||bundles<strings||lookups<bundles||lookups+(long)count*16>source.Length-20)throw new IOException("Native artwork BRT layout changed.");
        var rows=new List<Lookup>();var hashes=new HashSet<ulong>();
        for(int i=0;i<count;i++){
            int at=lookups+i*16;ulong hash=BitConverter.ToUInt64(source,at);if(!hashes.Add(hash))throw new IOException("Duplicate native BRT lookup.");
            rows.Add(new Lookup{Hash=hash,Bundle=BitConverter.ToUInt32(source,at+8),Name=BitConverter.ToUInt32(source,at+12)});
        }
        using(var names=new MemoryStream())using(var nw=new BinaryWriter(names))using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){
            nw.Write(source,strings,bundles-strings);
            foreach(var picture in pictures){
                byte[] name=Encoding.UTF8.GetBytes(picture.Tex.name);if(name.Length>255||names.Length>0x7FFFFF||picture.Kind.bundleLookupIndex>=bundleCount)throw new IOException("Native artwork BRT name/bundle limit exceeded.");
                uint reference=(uint)names.Length|0x800000u|((uint)name.Length<<24);nw.Write(uint.MaxValue);nw.Write(name);nw.Write((byte)0);
                foreach(string path in new[]{picture.Tex.name,picture.Tex.name.Substring(picture.Tex.name.LastIndexOf('/')+1)}){
                    ulong hash=Hash(path);if(!hashes.Add(hash))throw new IOException("New artwork collides with an existing BRT name.");
                    rows.Add(new Lookup{Hash=hash,Bundle=picture.Kind.bundleLookupIndex,Name=reference});
                }
            }
            Pad(nw,16);writer.Write(source,0,strings);writer.Write(names.ToArray());long newBundles=output.Position;
            writer.Write(source,bundles,lookups-bundles);long newLookups=output.Position;
            foreach(var row in rows.OrderBy(r=>r.Hash)){writer.Write(row.Hash);writer.Write(row.Bundle);writer.Write(row.Name);}
            Pad(writer,16);uint reloc=checked((uint)output.Position);writer.Write(source,source.Length-20,20);
            output.Position=8;writer.Write(newLookups);output.Position=24;writer.Write(newBundles);output.Position=72;writer.Write(rows.Count);
            resMeta=new byte[16];Buffer.BlockCopy(BitConverter.GetBytes(reloc),0,resMeta,0,4);Buffer.BlockCopy(BitConverter.GetBytes(20),0,resMeta,4,4);
            return output.ToArray();
        }
    }
    private static void Pad(BinaryWriter writer,int alignment){while(writer.BaseStream.Position%alignment!=0)writer.Write((byte)0);}
    private static ulong Hash(string value){ulong h=14695981039346656037UL;foreach(byte c in Encoding.UTF8.GetBytes(value.ToLowerInvariant()))h=unchecked((h*1099511628211UL)^c);return h;}
}
