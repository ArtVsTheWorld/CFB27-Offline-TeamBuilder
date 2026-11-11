using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using OfflineTeamBuilderRuntime;
using OfflineTeamBuilder.Repair;

internal static class UserDataChecks
{
    internal static void CheckAll() {
        Program.Reject(()=>RuntimePaths.DataDirectory(""),"missing Local AppData is reported instead of falling back to Plugins");
        Program.Reject(()=>RuntimePaths.DataDirectory("relative"),"relative Local AppData rejected");
        string sandbox=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"otb-readonly-install-"+Guid.NewGuid().ToString("N")));
        string install=Path.Combine(sandbox,"Program Files","MMC"),plugins=Path.Combine(install,"Plugins"),data=Path.Combine(sandbox,"User Data");
        Directory.CreateDirectory(plugins);
        string settings=Path.Combine(plugins,"OfflineTeamBuilderRuntime.settings.json");File.WriteAllText(settings,"{\"verboseLogging\":true}");
        var options=new RepairOptions {StoreDirectory=Path.Combine(sandbox,"Existing Asset Cache")};
        string config=Path.Combine(plugins,"OfflineTeamBuilderRuntime.repair.json");File.WriteAllBytes(config,RepairJson.Encode(options));
        string legacy=Path.Combine(plugins,"OfflineTeamBuilderRuntime");Directory.CreateDirectory(legacy);
        File.WriteAllText(Path.Combine(legacy,"runtime.log"),"legacy log retained");File.WriteAllText(Path.Combine(legacy,"status.json"),"{\"version\":\"2.1.6\"}");
        File.WriteAllBytes(Path.Combine(plugins,"Unrelated.dll"),new byte[]{1,2,3});
        byte[] before=File.ReadAllBytes(config);
        var store=new UserRuntimeStore(data,options.StoreDirectory);
        Program.Reject(()=>store.Resolve("../escape"),"user-data traversal rejected");
        Program.Reject(()=>store.Resolve("C:\\escape"),"absolute runtime destination rejected");
        Program.Reject(()=>store.Configure(new RepairOptions {StoreDirectory=Path.Combine(install,"cache")},plugins),"configured cache cannot write into MMC installation");
        Program.Assert(store.Configure(new RepairOptions(),plugins).StoreDirectory==options.StoreDirectory,"existing default asset cache retained");
        Program.Assert(store.Configure(new RepairOptions {StoreDirectory="cache"},plugins).StoreDirectory==Path.Combine(data,"cache"),"relative custom cache resolves under user data");
        Program.Reject(()=>new UserRuntimeStore(Path.Combine(plugins,"Data")).Prepare(plugins),"runtime root cannot be installed under Plugins");

        // Apply a real deny-write ACL only to a newly created private fixture.
        // Restore it in finally; no real MMC directory or user profile is edited.
        if(!sandbox.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Private ACL fixture escaped Temp.");
        var directory=new DirectoryInfo(install);DirectorySecurity original=directory.GetAccessControl(AccessControlSections.Access);
        var denied=new DirectorySecurity();denied.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(),AccessControlSections.Access);
        denied.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,FileSystemRights.Write|FileSystemRights.Delete|FileSystemRights.DeleteSubdirectoriesAndFiles,
            InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Deny));
        try {
            directory.SetAccessControl(denied);
            Program.Reject(()=>File.WriteAllText(Path.Combine(plugins,"must-not-write.txt"),"no"),"private Program Files fixture actually denies writes");
            store.Prepare(plugins);
            Program.Assert(File.ReadAllText(store.Resolve("OfflineTeamBuilderRuntime.settings.json")).Contains("true"),"settings migrated from read-only installation");
            Program.Assert(File.ReadAllBytes(store.Resolve("OfflineTeamBuilderRuntime.repair.json")).SequenceEqual(before),"repair configuration migration is byte-exact");
            Program.Assert(File.ReadAllBytes(config).SequenceEqual(before),"legacy configuration original preserved");
            Program.Assert(!File.Exists(store.Resolve("status.json")),"old status is archived, not published as current readiness");
            Program.Assert(Directory.GetFiles(store.Resolve("Migration"),"runtime.log",SearchOption.AllDirectories).Length==1,"legacy log archived in user-data migration folder");
            string helper=NativeHelperDeployment.Ensure(store.Resolve("Native"),NativeHelperDeployment.Payload(),NativeAssetGuards.ExpectedHash);
            Program.Assert(UserRuntimeStore.Inside(helper,data),"native helper deploys outside read-only install");
            var log=new RuntimeDiagnostics(null,data);log.Write("INFO","read-only install fixture");log.Status(0,"WaitingForGame",null);
            log.Startup(store.Prepare(plugins));
            Program.Assert(File.Exists(Path.Combine(data,"runtime.log"))&&File.Exists(Path.Combine(data,"status.txt")),"logs and readable status work with write-denied installation");
            Program.Assert(File.Exists(Path.Combine(data,"environment.json"))&&File.ReadAllText(Path.Combine(data,"environment.json")).Contains("pluginInventory"),"startup environment and DLL inventory are recorded outside installation");
            Program.Assert(!File.Exists(Path.Combine(plugins,"must-not-write.txt"))&&File.ReadAllBytes(Path.Combine(plugins,"Unrelated.dll")).SequenceEqual(new byte[]{1,2,3}),"protected install and unrelated files remain unchanged");
        }finally{var restore=new DirectorySecurity();restore.SetSecurityDescriptorBinaryForm(original.GetSecurityDescriptorBinaryForm(),AccessControlSections.Access);directory.SetAccessControl(restore);}
        File.WriteAllText(store.Resolve("OfflineTeamBuilderRuntime.settings.json"),"{\"verboseLogging\":false}");
        var second=new UserRuntimeStore(data,options.StoreDirectory);second.Prepare(plugins);
        Program.Assert(File.ReadAllText(store.Resolve("OfflineTeamBuilderRuntime.settings.json")).Contains("false"),"migration never overwrites current user settings");
        string otherPlugins=Path.Combine(sandbox,"Second MMC","Plugins");Directory.CreateDirectory(otherPlugins);
        File.WriteAllText(Path.Combine(otherPlugins,"OfflineTeamBuilderRuntime.settings.json"),"{\"verboseLogging\":true}");second.Prepare(otherPlugins);
        Program.Assert(File.ReadAllText(store.Resolve("OfflineTeamBuilderRuntime.settings.json")).Contains("false"),"second MMC shares authoritative settings without overwrite");
        Program.Assert(Directory.GetFiles(store.Resolve("Migration"),"*.bak",SearchOption.AllDirectories).Length>0,"conflicting legacy settings archived without deletion");
        Program.Assert(Encoding.UTF8.GetString(File.ReadAllBytes(config))==Encoding.UTF8.GetString(before),"all migration originals retained");
        string game=Path.Combine(sandbox,"Game"),manifest=Path.Combine(game,"ModData","Default","Data","mods.json"),managerConfig=Path.Combine(sandbox,"manager.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest));File.WriteAllText(manifest,"[{\"file_name\":\"OfflineTeamBuilder.fbmod\",\"version\":\"2.1.7\",\"private_config\":\"do-not-collect\"}]");
        File.WriteAllText(managerConfig,"{\"Games\":{\"CollegeFB27\":{\"Options\":{\"SelectedPack\":\"Default\"}}},\"secret\":\"do-not-collect\"}");
        string modReport=new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(RuntimeDiagnostics.ModContext(managerConfig,game));
        Program.Assert(modReport.Contains("OfflineTeamBuilder.fbmod")&&modReport.Contains("Default"),"selected pack and built mod names captured");
        Program.Assert(!modReport.Contains("do-not-collect"),"mod diagnostics exclude unrelated private configuration fields");
        string concurrent=Path.Combine(sandbox,"concurrent-config.json");var start=new System.Threading.ManualResetEventSlim(false);
        var first=System.Threading.Tasks.Task.Run(()=>{start.Wait();return UserRuntimeStore.PublishNew(concurrent,Encoding.UTF8.GetBytes("first"));});
        var secondWriter=System.Threading.Tasks.Task.Run(()=>{start.Wait();return UserRuntimeStore.PublishNew(concurrent,Encoding.UTF8.GetBytes("second"));});
        start.Set();System.Threading.Tasks.Task.WaitAll(first,secondWriter);start.Dispose();
        Program.Assert(first.Result!=secondWriter.Result,"concurrent config publication has one winner and never overwrites");
        string winner=File.ReadAllText(concurrent);
        Program.Assert(!UserRuntimeStore.PublishNew(concurrent,Encoding.UTF8.GetBytes("replacement"))&&File.ReadAllText(concurrent)==winner,"existing user configuration remains authoritative during publication races");
        Program.Assert(Directory.GetFiles(sandbox,"concurrent-config.json.*.tmp").Length==0,"concurrent config publication cleans only its own staging files");
    }
}
