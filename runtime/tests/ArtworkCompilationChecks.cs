using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using OfflineTeamBuilderRuntime;

internal static class ArtworkCompilationChecks
{
    internal static void CheckAll()
    {
        // Exact value observed in the 0.3.0 player-card crash, not a synthetic
        // null: ResourceRef's pointer branch attempted to release this RID.
        Program.Reject(()=>ArtworkCompiler.RequireResourceId(0x0569BFBC9AF2CFCEUL),"captured crashing PCB ID is rejected before packaging");
        ArtworkCompiler.RequireResourceId(0x0569BFBC9AF2CFCFUL);
        Program.Assert(true,"corrected captured PCB ID follows native ResourceRef tagging");
        string helper=Environment.GetEnvironmentVariable("OFFLINETB_CHECK_HOST");
        string root=Path.Combine(Path.GetTempPath(),"otba-compile-"+Guid.NewGuid().ToString("N"));
        var paths=new UserRuntimeStore(Path.Combine(root,"Data"));string plugin=Path.Combine(root,"MMC","Plugins","OfflineTeamBuilderRuntime.dll");
        var json=new JavaScriptSerializer();
        Dictionary<string,object> Run(){
            using(var child=Process.Start(DetachedHostLauncher.StartInfo(helper,"--prepare-artwork "+HostArguments.Encode(plugin)+" --private-root "+HostArguments.Encode(paths.Root)))) {
                if(!child.WaitForExit(90000)){child.Kill();throw new Exception("Private artwork compiler timed out.");}
                Program.Assert(child.ExitCode==0,"private artwork compiler exits successfully");
            }
            return json.Deserialize<Dictionary<string,object>>(File.ReadAllText(paths.Resolve("ArtworkCache/prepare-result.json")));
        }
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(plugin));
            var empty=Run();string mod=ArtworkCompiler.Output(paths);
            Program.Assert((bool)empty["ready"]&&(int)empty["pictures"]==0,"empty folder produces safe native placeholder");
            Program.Assert(ArtworkPreparation.IsOwnedMod(mod),"generated mod ownership is recognizable");
            string installed=Path.Combine(root,"MMC","Mods","CollegeFB27",ArtworkCompiler.ModName);Directory.CreateDirectory(Path.GetDirectoryName(installed));File.Copy(mod,installed);
            string team=paths.Resolve("Artwork/East Point");Directory.CreateDirectory(team);
            using(var image=new Bitmap(64,36)){using(var g=Graphics.FromImage(image))g.Clear(Color.ForestGreen);image.Save(Path.Combine(team,"pcb.png"),ImageFormat.Png);image.Save(Path.Combine(team,"tbak.png"),ImageFormat.Png);}
            var built=Run();string first=UserRuntimeStore.Hash(File.ReadAllBytes(mod));
            Program.Assert((int)built["pictures"]==2&&(int)built["pcbCount"]==1&&(int)built["tbakCount"]==1,"PCB and TBAK are independent native resources");
            Program.Assert((bool)built["nativeImageLibraries"]&&!(bool)built["xmlUsed"]&&!(bool)built["gameProcessOpened"],"compiler uses native libraries without XML or game access");
            DateTime stamp=File.GetLastWriteTimeUtc(mod);var cached=Run();
            Program.Assert((bool)cached["cached"]&&File.GetLastWriteTimeUtc(mod)==stamp&&UserRuntimeStore.Hash(File.ReadAllBytes(mod))==first,"unchanged input is byte-identical and not rewritten");
            var cache=Directory.GetFiles(paths.Resolve("ArtworkCache/Textures"),"*.bc7").First();byte[] corrupted=File.ReadAllBytes(cache);corrupted[0]^=1;File.WriteAllBytes(cache,corrupted);
            byte[] badmod=File.ReadAllBytes(mod);badmod[0]^=1;File.WriteAllBytes(mod,badmod);Run();
            Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(mod))==first,"damaged native texture and mod caches are rebuilt identically");
            using(var image=new Bitmap(64,36)){using(var g=Graphics.FromImage(image))g.Clear(Color.DarkOrange);image.Save(Path.Combine(team,"tbak.png"),ImageFormat.Png);}
            Run();Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(mod))!=first,"changed image changes mod content and version fingerprint");
            // Reimported regular files must get the complete output without
            // administrator privileges or a working symbolic link.
            {
                ArtworkPreparation.PublishGenerated(installed,mod,paths);
                Program.Assert((File.GetAttributes(installed)&FileAttributes.ReparsePoint)==0,"ordinary imported artwork file needs no symbolic link");
                Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(installed))==UserRuntimeStore.Hash(File.ReadAllBytes(mod)),"owned import receives complete compiled bytes");
                DateTime published=File.GetLastWriteTimeUtc(installed);ArtworkPreparation.PublishGenerated(installed,mod,paths);
                Program.Assert(File.GetLastWriteTimeUtc(installed)==published,"identical prepared artwork leaves imported timestamp unchanged");
                File.Delete(Path.Combine(team,"tbak.png"));Run();
                ArtworkPreparation.PublishGenerated(installed,mod,paths);
                Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(installed))==UserRuntimeStore.Hash(File.ReadAllBytes(mod)),"ordinary import updates after recompilation");
                File.Copy(Path.Combine(root,"Data","ArtworkCache","Backups","FirstUnifiedImport.fbmod"),installed,true);
                ArtworkPreparation.PublishGenerated(installed,mod,paths);
                Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(installed))==UserRuntimeStore.Hash(File.ReadAllBytes(mod)),"reimported old placeholder is refreshed on the next preparation");
                Program.Assert(Directory.GetFiles(paths.Resolve("ArtworkCache/Backups"),"*.fbmod").Length==1,"original artwork import is backed up once");
            }
            string unrelated=Path.Combine(root,"other.fbmod");File.WriteAllText(unrelated,"user mod");
            bool rejected=false;try{ArtworkPreparation.PublishGenerated(unrelated,mod,paths);}catch(IOException){rejected=true;}
            Program.Assert(rejected&&File.ReadAllText(unrelated)=="user mod","unrelated mod is never replaced");
            using(var image=new Bitmap(64,64)){
                using(var g=Graphics.FromImage(image)){g.Clear(Color.Transparent);g.FillRectangle(Brushes.ForestGreen,8,32,48,32);}
                image.Save(Path.Combine(team,"jersey.png"),ImageFormat.Png);
                image.Save(Path.Combine(team,"tbak.png"),ImageFormat.Png);
            }
            var withJersey=Run();var jerseyCounts=(Dictionary<string,object>)withJersey["counts"];
            Program.Assert((int)jerseyCounts["jersey"]==1,"jersey PNG adds an independent native library entry");
            Program.Assert((int)jerseyCounts["pcb"]==1&&(int)jerseyCounts["tbak"]==1,"one school compiles PCB TBAK and jersey together");
            ArtworkPreparation.PublishGenerated(installed,mod,paths);
            Program.Assert(UserRuntimeStore.Hash(File.ReadAllBytes(installed))==UserRuntimeStore.Hash(File.ReadAllBytes(mod)),"installed artwork receives the complete three-picture mod");
            Program.Assert(Directory.GetFiles(paths.Resolve("ArtworkCache/Textures"),"*-512x512-*.bc7").Any(f=>new FileInfo(f).Length==512*512),"portrait jersey is encoded at its native dimensions");
            string jerseyOnly=paths.Resolve("Artwork/Jersey Only");Directory.CreateDirectory(jerseyOnly);File.Copy(Path.Combine(team,"jersey.png"),Path.Combine(jerseyOnly,"teamjerseys.png"));
            var only=Run();jerseyCounts=(Dictionary<string,object>)only["counts"];
            Program.Assert((int)only["teams"]==2&&(int)jerseyCounts["jersey"]==2,"jersey-only team folder and filename alias work without PCB/TBAK");
            var repeat=Run();Program.Assert((bool)repeat["cached"],"unchanged jersey files reuse compiled artwork");
            File.WriteAllText(Path.Combine(jerseyOnly,"teamjerseys.png"),"bad image");var badJersey=Run();jerseyCounts=(Dictionary<string,object>)badJersey["counts"];
            Program.Assert((int)jerseyCounts["jersey"]==1&&(int)jerseyCounts["pcb"]==1,"invalid jersey is isolated from valid jersey and background inputs");
        }finally{
            string full=Path.GetFullPath(root),temporary=Path.GetFullPath(Path.GetTempPath());
            if(UserRuntimeStore.Inside(full,temporary)&&Path.GetFileName(full).StartsWith("otba-compile-",StringComparison.Ordinal)&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
