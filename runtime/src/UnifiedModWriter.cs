using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OfflineTeamBuilderRuntime;

// Combine the frozen TeamBuilder mod with freshly compiled artwork. Resource
// payloads remain byte-for-byte intact; only a shared legacy collector is joined.
internal static class UnifiedModWriter
{
    private const int LegacyHandler=-1113851035, LegacyEntrySize=52;
    private sealed class Entry {
        internal byte Type;internal int Index,Handler,HashOffset,SizeOffset;
        internal string Name,UserData;internal byte[] Record,Payload;
        internal string Key=>Type+":"+Name;
    }
    private sealed class Package {internal byte[] Header;internal string Profile,Title,Author;internal int Head;internal byte[] GameHash;internal List<Entry> Entries=new List<Entry>();}
    internal static byte[] Merge(byte[] teamBuilder,byte[] artwork)
    {
        var baseline=Read(teamBuilder);var overlay=Read(artwork);
        if(baseline.Profile!="CollegeFB27"||overlay.Profile!=baseline.Profile||baseline.Head!=overlay.Head||!baseline.GameHash.SequenceEqual(overlay.GameHash))throw new IOException("TeamBuilder and artwork target different game data.");
        if(baseline.Title!="Offline TeamBuilder"||overlay.Title!=baseline.Title||baseline.Author!=PluginIdentity.Authors||overlay.Author!=baseline.Author)throw new IOException("Unexpected unified mod identity.");
        var entries=new List<Entry>(baseline.Entries);var byKey=new Dictionary<string,Entry>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in entries)if(byKey.ContainsKey(entry.Key))throw new IOException("Duplicate resource in the base TeamBuilder mod.");else byKey.Add(entry.Key,entry);
        foreach(var entry in overlay.Entries) {
            if(entry.Type==0)continue; // Retain the original icon/screenshots.
            Entry previous;
            if(!byKey.TryGetValue(entry.Key,out previous)){entries.Add(entry);byKey.Add(entry.Key,entry);continue;}
            if(entry.Type!=3||entry.Handler!=LegacyHandler||previous.Handler!=LegacyHandler||entry.UserData!=previous.UserData||!entry.UserData.StartsWith("legacy;Collector (",StringComparison.Ordinal))throw new IOException("Unexpected overlapping TeamBuilder/artwork resource: "+entry.Name);
            byte[] merged=MergeCollector(previous.Payload,entry.Payload);
            previous.Payload=merged;
            using(var sha=SHA1.Create())Buffer.BlockCopy(sha.ComputeHash(merged),0,previous.Record,previous.HashOffset,20);
            Buffer.BlockCopy(BitConverter.GetBytes((long)merged.Length),0,previous.Record,previous.SizeOffset,8);
        }
        using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)) {
            writer.Write(overlay.Header);writer.Write(entries.Count);var payloads=new List<byte[]>();
            foreach(var entry in entries) {
                if(entry.Index!=-1){Buffer.BlockCopy(BitConverter.GetBytes(payloads.Count),0,entry.Record,1,4);payloads.Add(entry.Payload);}
                writer.Write(entry.Record);
            }
            long manifest=output.Position,offset=0;
            foreach(var payload in payloads){writer.Write(offset);writer.Write((long)payload.Length);offset+=payload.Length;}
            foreach(var payload in payloads)writer.Write(payload);
            output.Position=12;writer.Write(manifest);writer.Write(payloads.Count);return output.ToArray();
        }
    }
    private static byte[] MergeCollector(byte[] first,byte[] second)
    {
        if(first.Length%LegacyEntrySize!=0||second.Length%LegacyEntrySize!=0)throw new IOException("Malformed legacy collector patch.");
        var rows=new Dictionary<uint,byte[]>();var order=new List<uint>();
        foreach(var payload in new[]{first,second})for(int at=0;at<payload.Length;at+=LegacyEntrySize) {
            uint key=BitConverter.ToUInt32(payload,at);byte[] row=payload.Skip(at).Take(LegacyEntrySize).ToArray();byte[] previous;
            if(rows.TryGetValue(key,out previous)){if(!row.SequenceEqual(previous))throw new IOException("Both mods replace the same legacy UI asset.");}
            else{rows.Add(key,row);order.Add(key);}
        }
        return order.SelectMany(key=>rows[key]).ToArray();
    }
    private static Package Read(byte[] bytes)
    {
        if(bytes==null||bytes.Length<64||bytes.Length>512*1024*1024)throw new IOException("Invalid fbmod length.");
        using(var input=new MemoryStream(bytes,false))using(var reader=new BinaryReader(input)) {
            if(reader.ReadUInt64()!=72155812747760198UL||reader.ReadInt32()!=9)throw new IOException("Unsupported fbmod format.");
            long manifest=reader.ReadInt64();int payloadCount=Count(reader,65536);int profileLength=reader.ReadByte();
            var package=new Package{Profile=Encoding.UTF8.GetString(Take(reader,profileLength)),Head=reader.ReadInt32(),GameHash=Take(reader,20),Title=String(reader),Author=String(reader)};
            for(int i=0;i<4;i++)String(reader);
            package.Header=bytes.Take(checked((int)input.Position)).ToArray();int count=Count(reader,65536);
            for(int i=0;i<count;i++) {
                int start=checked((int)input.Position);var entry=new Entry{Type=reader.ReadByte(),Index=reader.ReadInt32(),Name=String(reader),UserData=""};
                if(entry.Index < -1 || entry.Index>=payloadCount)throw new IOException("fbmod payload index is outside its manifest.");
                if(entry.Index!=-1){entry.HashOffset=checked((int)input.Position-start);Take(reader,20);entry.SizeOffset=checked((int)input.Position-start);if(reader.ReadInt64()<0)throw new IOException("Negative resource size.");reader.ReadByte();entry.Handler=reader.ReadInt32();entry.UserData=String(reader);}
                SkipArray(reader);
                if(entry.Type==2){reader.ReadUInt32();reader.ReadUInt64();Take(reader,Count(reader,1024*1024));}
                else if(entry.Type==3){for(int j=0;j<5;j++)reader.ReadUInt32();if(entry.Handler==0)reader.ReadUInt64();reader.ReadInt32();SkipArray(reader);}
                else if(entry.Type==4){String(reader);reader.ReadInt32();}
                else if(entry.Type!=0&&entry.Type!=1&&entry.Type!=5)throw new IOException("Unknown fbmod resource type.");
                entry.Record=bytes.Skip(start).Take(checked((int)input.Position-start)).ToArray();package.Entries.Add(entry);
            }
            if(input.Position!=manifest||manifest<0||manifest>bytes.LongLength-16L*payloadCount)throw new IOException("Invalid fbmod manifest position.");
            long dataStart=manifest+16L*payloadCount;
            foreach(var entry in package.Entries) {
                if(entry.Index==-1)continue;
                input.Position=manifest+16L*entry.Index;long offset=reader.ReadInt64(),size=reader.ReadInt64();
                if(offset<0||size<0||size>int.MaxValue||offset>bytes.LongLength-dataStart-size)throw new IOException("Invalid fbmod payload bounds.");
                input.Position=dataStart+offset;entry.Payload=Take(reader,(int)size);
                // Legacy base resources can carry a logical/uncompressed hash
                // (and embedded icons use zero), rather than the CAS byte hash.
                // Preserve that metadata exactly. Collector payloads are raw
                // merge records, so their checksum is directly verifiable.
                if(entry.Handler==LegacyHandler)using(var sha=SHA1.Create())if(!sha.ComputeHash(entry.Payload).SequenceEqual(entry.Record.Skip(entry.HashOffset).Take(20)))throw new IOException("fbmod collector checksum mismatch.");
            }
            return package;
        }
    }
    private static int Count(BinaryReader reader,int maximum){int value=reader.ReadInt32();if(value<0||value>maximum)throw new IOException("Invalid fbmod collection length.");return value;}
    private static void SkipArray(BinaryReader reader){Take(reader,checked(Count(reader,65536)*4));}
    private static byte[] Take(BinaryReader reader,int count){byte[] result=reader.ReadBytes(count);if(result.Length!=count)throw new EndOfStreamException();return result;}
    private static string String(BinaryReader reader){using(var value=new MemoryStream()){for(int i=0;i<65536;i++){byte b=reader.ReadByte();if(b==0)return Encoding.UTF8.GetString(value.ToArray());value.WriteByte(b);}throw new IOException("fbmod string is too long.");}}
}
