using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

namespace OfflineTeamBuilderRuntime;

internal static class SharedUiPayload
{
    internal const string Resource = "OfflineTeamBuilderRuntime.SharedUiWorker.zip";

    internal static string Prepare(UserRuntimeStore store, CancellationToken token)
    {
        byte[] payload = DetachedHostLauncher.ResourceBytes(Resource);
        string root = store.Resolve("SharedUi/w/" + UserRuntimeStore.Hash(payload).Substring(0, 16));
        var worker = new UserRuntimeStore(root);
        using (var archive = new ZipArchive(new MemoryStream(payload), ZipArchiveMode.Read))
        {
            foreach (var entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                string path = worker.Resolve(entry.FullName);
                using (var input = entry.Open())
                using (var output = new MemoryStream())
                {
                    input.CopyTo(output);
                    byte[] expected = output.ToArray();
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(expected))
                        Install(worker, path, expected);
                }
            }
        }
        return root;
    }

    private static void Install(UserRuntimeStore worker, string destination, byte[] bytes)
    {
        // Keep the temporary name near the worker root so MMC's .NET Framework
        // process can publish nested stock inputs within Windows path limits.
        string pending = worker.Resolve(Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            UserRuntimeStore.PublishNew(pending, bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
                File.Replace(pending, destination, null);
            else
                File.Move(pending, destination);
        }
        finally
        {
            if (File.Exists(pending))
                File.Delete(pending);
        }
    }
}

