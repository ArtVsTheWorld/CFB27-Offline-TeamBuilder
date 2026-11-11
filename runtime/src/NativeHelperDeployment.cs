using System;
using System.IO;
using System.Security.Cryptography;

namespace OfflineTeamBuilderRuntime;

// One installation DLL; the unchanged native component is a plain resource.
// Publish a verified file in the plugin's normal data folder, then retain the
// existing Windows LoadLibrary path. No custom PE mapper or temporary execution.
internal static class NativeHelperDeployment
{
    internal const string ResourceName="OfflineTeamBuilderRuntime.NativePayload.dll";

    internal static byte[] Payload() {
        using(var stream=typeof(RuntimeAction).Assembly.GetManifestResourceStream(ResourceName)) {
            if(stream==null)throw new InvalidDataException("Embedded native helper is missing.");
            using(var output=new MemoryStream()) {stream.CopyTo(output);return output.ToArray();}
        }
    }

    private static void CheckHash(string hash) {
        if(hash==null||hash.Length!=64)throw new InvalidDataException("Invalid native helper identity.");
        foreach(char c in hash)if(!((c>='0'&&c<='9')||(c>='a'&&c<='f')||(c>='A'&&c<='F')))
            throw new InvalidDataException("Invalid native helper identity.");
    }

    internal static string PathFor(string root,string hash) {
        CheckHash(hash);
        return Path.Combine(Path.GetFullPath(root),hash.ToLowerInvariant(),"OfflineTeamBuilderNative.dll");
    }

    internal static string Ensure(string root,byte[] payload,string expected) {
        string target=PathFor(root,expected);
        if(payload==null)throw new InvalidDataException("Embedded native helper is missing.");
        using(var sha=SHA256.Create()) {
            string actual=BitConverter.ToString(sha.ComputeHash(payload)).Replace("-","");
            if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Embedded native helper failed its package integrity check.");
        }
        NativePe.ExportRva(payload,"Initialize");
        if(File.Exists(target)) {NativeAssetGuards.ValidateHelper(target,expected);return target;}
        string directory=Path.GetDirectoryName(target);Directory.CreateDirectory(directory);
        string temporary=Path.Combine(directory,"OfflineTeamBuilderNative."+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,8192,FileOptions.WriteThrough)) {
                file.Write(payload,0,payload.Length);file.Flush(true);
            }
            NativeAssetGuards.ValidateHelper(temporary,expected);
            try {File.Move(temporary,target);}
            catch(IOException) {if(!File.Exists(target))throw;NativeAssetGuards.ValidateHelper(target,expected);}
            NativeAssetGuards.ValidateHelper(target,expected);
            return target;
        } finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
}
