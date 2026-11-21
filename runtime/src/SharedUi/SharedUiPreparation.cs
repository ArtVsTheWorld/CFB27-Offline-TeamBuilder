using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using Frosty.Core;
using Frosty.Core.IO;
using FrostySdk;
using FrostySdk.Interfaces;

namespace OfflineTeamBuilderRuntime
{
    internal static class SharedUiPreparation
    {
        internal const string OverlayName = "TeamBuilderSharedUi.fbmod";
        internal const string OverlayTitle = "Offline TeamBuilder Shared UI (Automatic)";
        private const int PriorityConflict = 3;
        private static readonly object Sync = new object();
        private const string StandaloneAssembly = "TeamBuilderSharedUi.Launch";

        internal static void Prepare(ILogger logger, PluginManagerType type, CancellationToken token)
        {
            if (type != PluginManagerType.ModManager || !ProfilesLibrary.IsLoaded(ProfileVersion.CollegeFootball27))
                return;

            lock (Sync)
            {
                var app = Application.Current;
                if (app == null)
                    throw new OperationCanceledException("TeamBuilder Shared UI could not access the active MMC pack.");

                Snapshot CaptureCurrent() => app.Dispatcher.Invoke(new Func<Snapshot>(() => Capture(app.MainWindow)));
                var store = RuntimePaths.Current;
                var logSync = new object();
                void Log(string text)
                {
                    lock (logSync)
                    {
                        logger?.Log("[TeamBuilder Shared UI] " + text);
                        try
                        {
                            string logFile = store.Resolve("SharedUi/launch.log");
                            Directory.CreateDirectory(Path.GetDirectoryName(logFile));
                            if (File.Exists(logFile) && new FileInfo(logFile).Length > 2 * 1024 * 1024)
                            {
                                string previous = store.Resolve("SharedUi/launch.log.previous");
                                File.Delete(previous);
                                File.Move(logFile, previous);
                            }
                            File.AppendAllText(logFile, DateTime.UtcNow.ToString("o") + " " + text + Environment.NewLine);
                        }
                        catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }

                bool duplicate = AppDomain.CurrentDomain.GetAssemblies().Any(assembly =>
                        IsStandaloneAssembly(assembly.GetName().Name)) ||
                    File.Exists(Path.Combine(RuntimePaths.PluginDirectory, StandaloneAssembly + ".dll"));
                RunForPack(CaptureCurrent, store, duplicate, token, Log);
            }
        }

        internal static bool IsStandaloneAssembly(string name) =>
            string.Equals(name, StandaloneAssembly, StringComparison.OrdinalIgnoreCase);

        internal static void RunForPack(Func<Snapshot> capture, UserRuntimeStore store,
            bool standalonePresent, CancellationToken token, Action<string> log)
        {
            token.ThrowIfCancellationRequested();
            Snapshot snapshot;
            try
            {
                snapshot = capture();
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new OperationCanceledException("TeamBuilder Shared UI could not read the active pack. " + ex.Message, ex);
            }
            if (snapshot.OverlayPath == null)
            {
                log("INACTIVE: companion fbmod is not enabled in this pack.");
                return;
            }
            if (standalonePresent)
                throw new OperationCanceledException("Close MMC and disable TeamBuilderSharedUi.Launch.dll before using the integrated Shared UI builder.");

            try
            {
                ValidateIdentity(snapshot.OverlayPath);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                throw new OperationCanceledException("TeamBuilder Shared UI could not verify the companion. " + ex.Message, ex);
            }

            string dataRoot = store.Resolve("SharedUi");
            try
            {
                using (var lease = Acquire(dataRoot, token))
                {
                    if (!snapshot.Files.Contains("OfflineTeamBuilder.fbmod", StringComparer.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Enable OfflineTeamBuilder.fbmod first.");
                    string worker = SharedUiPayload.Prepare(store, token);
                    var workerStore = new UserRuntimeStore(worker);
                    string input = workerStore.Resolve("Builder/Evidence/live-pack.json");
                    UserRuntimeStore.Atomic(input, Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(snapshot.ToConfig())));
                    string python = workerStore.Resolve("Python/python.exe");
                    string script = workerStore.Resolve("Builder/Tools/auto_launch.py");
                    log("BUILD: " + snapshot.Name + "; " + snapshot.Files.Count + " enabled files; live pack order captured.");
                    int result = RunProcess(python, new[] { "-B", script, "--sdk", snapshot.ManagerRoot, "--config", input },
                        worker, token, log);
                    token.ThrowIfCancellationRequested();
                    if (result == PriorityConflict)
                    {
                        if (snapshot.Identity != capture().Identity)
                            throw new OperationCanceledException("The active pack changed during the priority check. Launch again.");
                        ValidateIdentity(snapshot.OverlayPath);
                        Neutralize(snapshot.OverlayPath, snapshot.ManagerRoot);
                        log("SKIPPED: Higher-priority mods replace Shared UI assets. Launching without the companion's changes. Move it above the listed mods to apply them.");
                        return;
                    }
                    if (result != 0)
                        throw new InvalidOperationException("The structural builder failed (exit " + result + "). See SharedUi/launch.log.");

                    Snapshot current = capture();
                    if (snapshot.Identity != current.Identity)
                        throw new OperationCanceledException("The active pack changed during the build. Launch again.");

                    string candidate = workerStore.Resolve("Builder/Release/" + OverlayName);
                    ValidateCandidate(candidate);
                    ValidateIdentity(snapshot.OverlayPath);
                    bool changed = Publish(candidate, snapshot.OverlayPath);
                    log((changed ? "UPDATED" : "UNCHANGED") + ": companion fbmod is ready before MMC compilation.");
                }
            }
            catch (OperationCanceledException)
            {
                log("CANCELLED: launch stopped before publishing a companion for the changed or cancelled pack.");
                throw;
            }
            catch (Exception ex)
            {
                log("DISABLED: " + ex.Message);
                try
                {
                    using (var lease = Acquire(dataRoot, token))
                    {
                        if (snapshot.Identity != capture().Identity)
                            throw new OperationCanceledException("The active pack changed before cleanup. Launch again.");
                        ValidateIdentity(snapshot.OverlayPath);
                        Neutralize(snapshot.OverlayPath, snapshot.ManagerRoot);
                    }
                    log("NEUTRAL: outdated generated overlay cleared.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception error)
                {
                    log("CANNOT_NEUTRALIZE: " + error.Message);
                    throw new OperationCanceledException("TeamBuilder Shared UI could not clear its outdated companion. See SharedUi/launch.log.", error);
                }
                throw new OperationCanceledException("TeamBuilder Shared UI preparation did not complete. See SharedUi/launch.log.", ex);
            }
        }

        internal sealed class Snapshot
        {
            public string Name,ManagerRoot,GamePath,OverlayPath;
            public readonly List<string> Files=new List<string>();
            public string Identity { get; private set; }
            public void Seal() { Identity=ManagerRoot+"\n"+GamePath+"\n"+Name+"\n"+string.Join("\n",Files.Select(f=>{
                var file=new FileInfo(Path.Combine(ManagerRoot,"Mods","CollegeFB27",f));
                return f+"|"+(file.Exists?file.Length+"|"+file.LastWriteTimeUtc.Ticks:"missing");
            })); }
            public object ToConfig() => new { Games=new { CollegeFB27=new { GamePath,Options=new { SelectedPack=Name },Packs=new Dictionary<string,string> { [Name]=string.Join("|",Files.Select(f=>f+":True")) } } } };
        }
        internal static object Member(object obj,string name)
        {
            if(obj==null)return null;
            var flags=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
            for(Type t=obj.GetType();t!=null;t=t.BaseType)
            {
                var p=t.GetProperty(name,flags|BindingFlags.DeclaredOnly);if(p!=null)return p.GetValue(obj,null);
                var f=t.GetField(name,flags|BindingFlags.DeclaredOnly);if(f!=null)return f.GetValue(obj);
            }
            return null;
        }
        internal static Snapshot Capture(object owner,string managerRoot=null,string gamePath=null)
        {
            var pack=Member(owner,"selectedPack");
            var entries=Member(pack,"AppliedMods") as IEnumerable;
            if(entries==null)throw new InvalidOperationException("MMC live selectedPack.AppliedMods is unavailable.");
            string manager=Path.GetFullPath(managerRoot??AppDomain.CurrentDomain.BaseDirectory);
            var snapshot=new Snapshot { Name=Convert.ToString(Member(pack,"Name")),ManagerRoot=manager,GamePath=gamePath??Config.Get("GamePath","",ConfigScope.Game) };
            var enabledEntries=entries.Cast<object>().Where(e=>Member(e,"IsEnabled") is bool yes && yes).ToArray();
            bool ownEnabled=enabledEntries.Any(e=>{
                var mod=Member(e,"Mod");string file=Convert.ToString(Member(mod,"Filename"));
                return file.Equals(OverlayName,StringComparison.OrdinalIgnoreCase)||Convert.ToString(Member(Member(mod,"ModDetails"),"Title"))==OverlayTitle;
            });
            if(!ownEnabled){snapshot.Seal();return snapshot;}
            int ownCount=0;
            foreach(object entry in enabledEntries)
            {
                if(!(Member(entry,"IsEnabled") is bool enabled) || !enabled)continue;
                if(!(Member(entry,"IsFound") is bool found) || !found)throw new InvalidOperationException("An enabled mod is missing.");
                var mod=Member(entry,"Mod");string file=Convert.ToString(Member(mod,"Filename"));
                if(string.IsNullOrEmpty(file) || Path.GetFileName(file)!=file)throw new InvalidOperationException("Unsupported mod filename.");
                snapshot.Files.Add(file);
                string title=Convert.ToString(Member(Member(mod,"ModDetails"),"Title"));
                if(file.Equals(OverlayName,StringComparison.OrdinalIgnoreCase) || title==OverlayTitle)
                {
                    ownCount++;
                    snapshot.OverlayPath=Path.Combine(manager,"Mods","CollegeFB27",file);
                }
            }
            if(ownCount>1)throw new InvalidOperationException("Disable duplicate structural TeamBuilder UI overlays.");
            if(string.IsNullOrWhiteSpace(snapshot.Name) || string.IsNullOrWhiteSpace(snapshot.GamePath))throw new InvalidOperationException("Pack name or game path is missing.");
            snapshot.Seal();
            return snapshot;
        }
        internal sealed class Lease : IDisposable
        {
            private Mutex mutex;
            public Lease(Mutex value){mutex=value;}
            public void Dispose(){if(mutex!=null){mutex.ReleaseMutex();mutex.Dispose();mutex=null;}}
        }
        internal static Lease Acquire(string root,CancellationToken token)
        {
            string key;using(var hash=SHA256.Create())key=BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(root).ToUpperInvariant()))).Replace("-","");
            var mutex=new Mutex(false,"Local\\TeamBuilderSharedUi-"+key);var timer=Stopwatch.StartNew();
            try { while(true){token.ThrowIfCancellationRequested();try{if(mutex.WaitOne(100))return new Lease(mutex);}catch(AbandonedMutexException){return new Lease(mutex);}
                if(timer.Elapsed>TimeSpan.FromSeconds(45))throw new OperationCanceledException("Another MMC instance is preparing TeamBuilder UI. Try again when it finishes.");} }
            catch{mutex.Dispose();throw;}
        }
        internal static string Quote(string value)
        {
            if(value.IndexOf('\0')>=0)throw new ArgumentException("NUL in argument");
            var b=new StringBuilder("\"");int slashes=0;
            foreach(char c in value){if(c=='\\'){slashes++;continue;}if(c=='\"'){b.Append('\\',slashes*2+1);b.Append(c);slashes=0;continue;}b.Append('\\',slashes);slashes=0;b.Append(c);}
            b.Append('\\',slashes*2);return b.Append('\"').ToString();
        }
        internal static int RunProcess(string file,IEnumerable<string> args,string cwd,CancellationToken token,Action<string> log)
        {
            using(var job=new ChildJob())
            using(var p=new Process { StartInfo=new ProcessStartInfo(file,string.Join(" ",args.Select(Quote))) { WorkingDirectory=cwd,UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 } })
            {
                p.StartInfo.EnvironmentVariables["PYTHONNOUSERSITE"]="1";
                p.StartInfo.EnvironmentVariables["PYTHONIOENCODING"]="utf-8";
                p.OutputDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};p.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)log(e.Data);};
                p.Start();job.Attach(p);p.BeginOutputReadLine();p.BeginErrorReadLine();
                var timer=Stopwatch.StartNew();
                while(!p.WaitForExit(100))
                {
                    if(token.IsCancellationRequested || timer.Elapsed>TimeSpan.FromSeconds(45))
                    { job.Stop();try { p.WaitForExit(5000); } catch { } token.ThrowIfCancellationRequested();throw new TimeoutException("TeamBuilder Shared UI preparation exceeded 45 seconds."); }
                }
                p.WaitForExit();return p.ExitCode;
            }
        }
        private static void ValidateIdentity(string file)
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The companion is linked to another location.");
            using (var reader = new FrostyModReader(File.OpenRead(file)))
            {
                if (!reader.IsValid)
                    throw new InvalidDataException("The installed companion is not a valid fbmod.");
                var details = reader.ReadModDetails();
                if (details.Title != OverlayTitle || details.Author != "JDHalfRack / Ace / Timberline Tex")
                    throw new InvalidDataException("The installed companion filename belongs to a different mod.");
            }
        }
        internal static void ValidateCandidate(string file)
        {
            using(var reader=new FrostyModReader(File.OpenRead(file)))
            {
                if(!reader.IsValid)throw new InvalidDataException("MMC rejected the generated overlay.");
                var details=reader.ReadModDetails();
                if(details.Title!=OverlayTitle||details.Author!="JDHalfRack / Ace / Timberline Tex")throw new InvalidDataException("Unexpected generated mod identity.");
                foreach(var r in reader.ReadResources())if(r.ResourceIndex>=0)
                {
                    if(r.Type.ToString()!="Chunk" || !r.UserData.StartsWith("legacy;"))throw new InvalidDataException("Unexpected generated resource.");
                    reader.GetResourceData(r);
                }
            }
        }
        internal static bool Publish(string candidate,string destination)
        {
            byte[] data=File.ReadAllBytes(candidate);
            if(File.Exists(destination) && File.ReadAllBytes(destination).SequenceEqual(data))return false;
            string tmp=destination+"."+Guid.NewGuid().ToString("N")+".autobuild.tmp";
            try { File.WriteAllBytes(tmp,data);
                if(File.Exists(destination))File.Replace(tmp,destination,null);else File.Move(tmp,destination); }
            finally { if(File.Exists(tmp))File.Delete(tmp); }
            return true;
        }
        internal static void Neutralize(string destination,string managerRoot=null)
        {
            // A resource-free current-profile mod prevents stale ASTs being reused.
            string cache=Path.Combine(managerRoot??AppDomain.CurrentDomain.BaseDirectory,"Caches","collegefootball27.cache");
            byte[] head=new byte[40];using(var f=File.OpenRead(cache)){if(f.Read(head,0,40)!=40)throw new InvalidDataException("Invalid MMC cache header.");}
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream))
            {
                Action<string> s=value=>{w.Write(Encoding.UTF8.GetBytes(value));w.Write((byte)0);};
                w.Write((ulong)72155812747760198);w.Write((uint)9);w.Write((long)0);w.Write(0);w.Write((byte)11);w.Write(Encoding.ASCII.GetBytes("CollegeFB27"));w.Write(head,16,24);
                s(OverlayTitle);s("JDHalfRack / Ace / Timberline Tex");s("Dynasty");s("0.1.0 auto inactive "+BitConverter.ToUInt32(head,16));s("Automatic TeamBuilder UI companion. Enable the companion with the integrated Offline TeamBuilder Runtime.");s("");
                w.Write(5);foreach(var name in new[]{"Icon","Screenshot0","Screenshot1","Screenshot2","Screenshot3"}){w.Write((byte)0);w.Write(-1);s(name);w.Write(0);}
                long end=stream.Position;stream.Position=12;w.Write(end);stream.Position=end;w.Flush();
                string temp=destination+".neutral.tmp";try{File.WriteAllBytes(temp,stream.ToArray());Publish(temp,destination);}finally{if(File.Exists(temp))File.Delete(temp);}
            }
        }
    }
}



