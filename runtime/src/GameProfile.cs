using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OfflineTeamBuilderRuntime;

// This embedded profile is the update boundary. It is not a writable runtime
// override: changing native layouts requires rebuilding and contract tests.
internal sealed class GameProfile
{
    private static readonly Lazy<GameProfile> current = new Lazy<GameProfile>(Load);
    internal static GameProfile Current { get { return current.Value; } }
    public string id;
    public string description;
    public Dictionary<string,string> symbols;
    public Dictionary<string,NativeTemplate> templates;
    public NativeCheck[] hooks;
    public NativeCheck[] validations;
    public NativeCheck[] mainPrefixes;
    public LifecycleSlot[] lifecycle;
    public TransportSpec[] transport;

    private static GameProfile Load()
    {
        using (Stream stream = typeof(GameProfile).Assembly.GetManifestResourceStream("OfflineTeamBuilderRuntime.Profile.json"))
        using (StreamReader reader = new StreamReader(stream ?? throw new InvalidDataException("Embedded native profile is missing."), Encoding.UTF8))
        {
            GameProfile profile = new JavaScriptSerializer { MaxJsonLength = 1024*1024 }.Deserialize<GameProfile>(reader.ReadToEnd());
            profile.Validate();
            return profile;
        }
    }
    internal long Rva(string name)
    {
        string value;
        if (symbols == null || !symbols.TryGetValue(name,out value)) throw new InvalidDataException("Unknown native symbol: "+name);
        return Parse(value);
    }
    internal static long Parse(string value)
    {
        return value.StartsWith("0x",StringComparison.OrdinalIgnoreCase)
            ? long.Parse(value.Substring(2),NumberStyles.HexNumber,CultureInfo.InvariantCulture)
            : long.Parse(value,CultureInfo.InvariantCulture);
    }
    internal static byte[] Hex(string value)
    {
        value=value.Replace(" ","");
        if (value.Length%2!=0) throw new InvalidDataException("Odd native byte string.");
        byte[] bytes=new byte[value.Length/2];
        for(int n=0;n<bytes.Length;n++) bytes[n]=byte.Parse(value.Substring(n*2,2),NumberStyles.HexNumber,CultureInfo.InvariantCulture);
        return bytes;
    }
    internal bool FitsImage(long size)
    {
        // Image SIZE is a bounds check, not an identity/hash allow-list.
        long required=symbols.Values.Select(Parse).Max()+0x1000;
        return size>=required && size<=0x40000000;
    }
    internal byte[] Emit(string name,params ulong[] args) {
        return LogoRegistryRepair.Apply(name,NativeWorkAdmission.Apply(name,templates[name].Emit(this,args),this,args),this,args);
    }
    internal void Validate()
    {
        if(string.IsNullOrWhiteSpace(id)||symbols==null||symbols.Count==0||templates==null||hooks==null||validations==null||mainPrefixes==null||lifecycle==null||transport==null)
            throw new InvalidDataException("Incomplete native compatibility profile.");
        foreach(string key in symbols.Keys) if(Rva(key)<0||Rva(key)>=0x40000000) throw new InvalidDataException("Native RVA bounds: "+key);
        foreach(var template in templates.Values) template.Validate(this);
        foreach(var h in hooks.Concat(validations).Concat(mainPrefixes))
        { Rva(h.target); if(Hex(h.expected).Length==0) throw new InvalidDataException("Empty native signature."); }
        if(hooks.Select(x=>x.name).Distinct().Count()!=hooks.Length) throw new InvalidDataException("Duplicate hook names.");
        if(transport.Length!=3||transport.Select(x=>x.name).Distinct().Count()!=transport.Length) throw new InvalidDataException("Transport hooks must have three unique names.");
        foreach(var hook in transport) {
            long rva=Parse(hook.rva);
            if(string.IsNullOrWhiteSpace(hook.name)||rva<0||rva>=0x40000000||Hex(hook.expected).Length<14||
                (hook.kind!="key"&&hook.kind!="gate3"&&hook.kind!="protohttplocalurl"))
                throw new InvalidDataException("Invalid transport hook contract: "+hook.name);
        }
        foreach(var slot in lifecycle) { Rva(slot.target); if(slot.slot<0||slot.slot%8!=0||slot.slot>0x2000) throw new InvalidDataException("Vtable slot bounds."); }
    }
    // Caller supplies a read-only RVA reader. All mandatory contracts are
    // checked before any subsystem is allowed to modify the game.
    internal List<string> CheckNative(long size,ulong image,Func<long,int,byte[]> read)
    {
        var errors=new List<string>();
        if(!FitsImage(size)) { errors.Add("IMAGE_BOUNDS: native profile does not fit this loaded image."); return errors; }
        foreach(var h in hooks.Concat(validations).Concat(mainPrefixes))
        {
            byte[] expected=Hex(h.expected), actual=read(Rva(h.target),expected.Length);
            if(actual==null||!actual.SequenceEqual(expected)) errors.Add("SIGNATURE_MISMATCH "+(h.name??h.target)+" at "+symbols[h.target]+"; no hooks installed.");
        }
        byte[] dispatcher=read(Rva("Dispatcher"),16);
        if(dispatcher==null||!dispatcher.SequenceEqual(Hex("48895c2408488974241048897c241855"))) errors.Add("SIGNATURE_MISMATCH native dispatcher.");
        foreach(var slot in lifecycle)
        {
            byte[] value=read(Rva("ServiceVtable")+slot.slot,8);
            if(value==null||value.Length!=8||BitConverter.ToUInt64(value,0)!=image+(ulong)Rva(slot.target))
                errors.Add("VTABLE_MISMATCH "+slot.target+" at slot +0x"+slot.slot.ToString("X")+"; another plugin or unsupported native layout.");
        }
        return errors;
    }
}
internal sealed class NativeTemplate
{
    public string code;
    public Relocation[] relocations;
    internal void Validate(GameProfile p)
    {
        byte[] bytes=GameProfile.Hex(code); var used=new HashSet<int>();
        foreach(var r in relocations)
        {
            if(r.offset<0||r.offset>bytes.Length-8||r.arg<0||r.arg>7) throw new InvalidDataException("Native relocation bounds.");
            for(int n=0;n<8;n++) if(!used.Add(r.offset+n)) throw new InvalidDataException("Overlapping native relocations.");
            r.Add(p);
        }
    }
    internal byte[] Emit(GameProfile p,ulong[] args)
    {
        byte[] bytes=GameProfile.Hex(code);
        foreach(var r in relocations)
        {
            if(r.arg>=args.Length) throw new ArgumentException("Missing native relocation argument.");
            byte[] value=BitConverter.GetBytes(unchecked(args[r.arg]+(ulong)r.Add(p)));
            Buffer.BlockCopy(value,0,bytes,r.offset,8);
        }
        return bytes;
    }
}
internal sealed class Relocation
{
    public int offset;
    public int arg;
    public string add;
    public string target;
    public string anchor;
    internal long Add(GameProfile p) { return GameProfile.Parse(add??"0")+(target==null?0:p.Rva(target))-(anchor==null?0:p.Rva(anchor)); }
}
internal sealed class NativeCheck { public string name; public string target; public int offset; public string expected; }
internal sealed class LifecycleSlot { public int slot; public string target; }
internal sealed class TransportSpec { public string name; public string rva; public string expected; public string kind; }
