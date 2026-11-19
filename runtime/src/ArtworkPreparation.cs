using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using FrostySdk.Interfaces;

namespace OfflineTeamBuilderRuntime;

// MMC collects the enabled filenames before PreLaunch, then reads their contents
// afterward. Refresh the single TeamBuilder entry with the base plus artwork.
internal static class ArtworkPreparation
{
    internal static void Prepare(ILogger logger,CancellationToken token)
    {
        string installed=Path.Combine(UserRuntimeStore.InstallDirectory(RuntimePaths.PluginDirectory),"Mods","CollegeFB27",ArtworkCompiler.ModName);
        if(!File.Exists(installed))return;
        try {
            if(!IsOwnedMod(installed))throw new IOException("The TeamBuilder filename belongs to an unrecognized mod; it was left unchanged.");
            string helper=DetachedHostLauncher.VerifyInstalled();
            logger?.Log("[Offline TeamBuilder] Preparing folder artwork before launch. New pictures take longer; unchanged pictures use the cache.");
            using(var child=Process.Start(DetachedHostLauncher.StartInfo(helper,"--prepare-artwork "+HostArguments.Encode(RuntimeHostIdentity.PluginPath)))) {
                var timer=Stopwatch.StartNew();
                while(!child.WaitForExit(250)) {
                    if(token.IsCancellationRequested||timer.ElapsedMilliseconds>300000){try{child.Kill();}catch{}throw new IOException("Artwork preparation was cancelled or timed out; the previous artwork remains available.");}
                }
                if(child.ExitCode!=0)throw new IOException("Artwork preparation failed. See ArtworkCache/prepare-result.json in LocalAppData. The previous artwork was retained.");
            }
            PublishGenerated(installed,ArtworkCompiler.Output(RuntimePaths.Current),RuntimePaths.Current);
            logger?.Log("[Offline TeamBuilder] Folder artwork ready. Native image libraries will load with the game.");
        } catch(Exception ex) {
            logger?.Log("[Offline TeamBuilder] ARTWORK_NOT_UPDATED "+ex.Message);
        }
    }
    internal static bool IsOwnedMod(string path)
    {
        using(var stream=File.OpenRead(path))using(var reader=new BinaryReader(stream)) {
            if(reader.ReadUInt64()!=72155812747760198UL||reader.ReadInt32()!=9)return false;
            reader.ReadInt64();reader.ReadInt32();int profileLength=reader.ReadByte();
            if(Encoding.UTF8.GetString(reader.ReadBytes(profileLength))!="CollegeFB27")return false;
            reader.ReadInt32();reader.ReadBytes(20);
            return ReadString(reader)==ArtworkCompiler.Title&&ReadString(reader)==PluginIdentity.Authors;
        }
    }
    private static string ReadString(BinaryReader reader)
    {
        using(var buffer=new MemoryStream()){for(int i=0;i<2048;i++){byte b=reader.ReadByte();if(b==0)return Encoding.UTF8.GetString(buffer.ToArray());buffer.WriteByte(b);}throw new IOException("Artwork mod header is too long.");}
    }
    internal static void PublishGenerated(string installed,string generated,UserRuntimeStore paths)
    {
        if(Path.GetFileName(installed)!=ArtworkCompiler.ModName||!IsOwnedMod(installed)||!IsOwnedMod(generated))throw new IOException("Artwork installation identity mismatch.");
        if((File.GetAttributes(installed)&FileAttributes.ReparsePoint)!=0) {
            using(var stream=File.OpenRead(installed)) {
                var target=new StringBuilder(1024);uint n=GetFinalPathNameByHandle(stream.SafeFileHandle.DangerousGetHandle(),target,(uint)target.Capacity,0);
                string actual=target.ToString();if(actual.StartsWith(@"\\?\"))actual=actual.Substring(4);
                if(n==0||n>=target.Capacity||!string.Equals(Path.GetFullPath(actual),Path.GetFullPath(generated),StringComparison.OrdinalIgnoreCase))throw new IOException("The artwork mod is linked somewhere else; it was left unchanged.");
            }
            return;
        }
        // MMC imports can turn an earlier symbolic link back into an ordinary
        // file. Publish the complete generated mod directly; this needs no link
        // privilege and works after a reimport. The cache remains in user data.
        byte[] original=File.ReadAllBytes(installed);
        byte[] compiled=File.ReadAllBytes(generated);
        if(UserRuntimeStore.Hash(original)==UserRuntimeStore.Hash(compiled))return;
        string backup=paths.Resolve("ArtworkCache/Backups/FirstUnifiedImport.fbmod");
        if(!File.Exists(backup))UserRuntimeStore.PublishNew(backup,original);
        string temporary=installed+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllBytes(temporary,compiled);
            File.Replace(temporary,installed,null);
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern uint GetFinalPathNameByHandle(IntPtr file,StringBuilder path,uint capacity,uint flags);
}
