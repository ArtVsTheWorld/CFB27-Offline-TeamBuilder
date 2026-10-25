using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OfflineTeamBuilder.Repair;

internal sealed class LocalAssetStore : IDisposable
{
	private readonly object sync = new object();

	private readonly RepairOptions options;

	private readonly string root;

	private readonly string objects;

	private readonly string manifests;

	private readonly string quarantine;

	private readonly Dictionary<string, AssetRecord> records = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);

	private readonly FileStream ownership;

	private readonly HashSet<string> completeGroups = new HashSet<string>(StringComparer.Ordinal);

	public readonly List<string> RecoveryWarnings = new List<string>();

	public int CompleteGroups
	{
		get
		{
			lock (sync)
			{
				return completeGroups.Count;
			}
		}
	}

	public string Root => root;

	public LocalAssetStore(RepairOptions options)
	{
		if (string.IsNullOrWhiteSpace(options.StoreDirectory) || !Path.IsPathRooted(options.StoreDirectory))
		{
			throw new InvalidDataException("StoreDirectory must be an absolute path.");
		}
		this.options = options;
		root = Path.GetFullPath(options.StoreDirectory);
		objects = Path.Combine(root, "objects");
		manifests = Path.Combine(root, "manifests");
		quarantine = Path.Combine(root, "quarantine");
		Directory.CreateDirectory(objects);
		Directory.CreateDirectory(manifests);
		Directory.CreateDirectory(quarantine);
		Directory.CreateDirectory(Path.Combine(root, "staging"));
		ownership = new FileStream(Path.Combine(root, "store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		try
		{
			Recover();
		}
		catch
		{
			ownership.Dispose();
			throw;
		}
	}

	private void Recover()
	{
		foreach (string item in Directory.EnumerateFiles(manifests, "*.json"))
		{
			try
			{
				if (new FileInfo(item).Length > 1048576)
				{
					throw new InvalidDataException("Oversized manifest.");
				}
				AssetRecord assetRecord = RepairJson.Decode<AssetRecord>(File.ReadAllBytes(item));
				RepairJson.Identify(assetRecord.Id, out var kind, out var group);
				if (assetRecord.Schema != 1 || assetRecord.Kind != kind || assetRecord.Group != group || !RepairJson.IsHash(assetRecord.Sha256) || assetRecord.Length <= 0 || assetRecord.UploadPaths == null)
				{
					throw new InvalidDataException("Invalid manifest fields.");
				}
				string text = ObjectPath(assetRecord.Sha256);
				if (!File.Exists(text) || new FileInfo(text).Length != assetRecord.Length || RepairJson.HashFile(text) != assetRecord.Sha256)
				{
					throw new InvalidDataException("Missing/corrupt stored object.");
				}
				if (Path.GetFileNameWithoutExtension(item) != RepairJson.Hash(Encoding.UTF8.GetBytes(assetRecord.Id)))
				{
					throw new InvalidDataException("Manifest identity mismatch.");
				}
				records.Add(assetRecord.Id, assetRecord);
			}
			catch (Exception ex)
			{
				RecoveryWarnings.Add(Path.GetFileName(item) + ": " + ex.Message);
			}
		}
		foreach (string item2 in records.Values.Select((AssetRecord r) => r.Group).Distinct())
		{
			RefreshGroup(item2);
		}
	}

	public string NewStagingFile()
	{
		return Path.Combine(root, "staging", Guid.NewGuid().ToString("N") + ".part");
	}

	private string ObjectPath(string hash)
	{
		if (!RepairJson.IsHash(hash))
		{
			throw new InvalidDataException("Invalid content hash.");
		}
		return Path.Combine(objects, hash + ".blob");
	}

	private string ManifestPath(string id)
	{
		return Path.Combine(manifests, RepairJson.Hash(Encoding.UTF8.GetBytes(id)) + ".json");
	}

	private long UsedBytes(string directory)
	{
		long num = 0L;
		foreach (string item in Directory.EnumerateFiles(directory))
		{
			num = checked(num + new FileInfo(item).Length);
		}
		return num;
	}

	public AssetRecord Get(string id)
	{
		lock (sync)
		{
			AssetRecord value;
			return records.TryGetValue(id, out value) ? value : null;
		}
	}

	public FileStream OpenVerified(AssetRecord r)
	{
		lock (sync)
		{
			string text = ObjectPath(r.Sha256);
			if (!File.Exists(text) || new FileInfo(text).Length != r.Length || RepairJson.HashFile(text) != r.Sha256)
			{
				throw new ProtocolError(500, "Stored object failed integrity verification; it will not be served.");
			}
			return new FileStream(text, FileMode.Open, FileAccess.Read, FileShare.Read);
		}
	}

	public string Commit(MetadataRecord metadata, string temporary, string uploadPath, string validation, out bool replay, out bool revised)
	{
		long length = new FileInfo(temporary).Length;
		string text = RepairJson.HashFile(temporary);
		lock (sync)
		{
			revised = false;
			replay = records.TryGetValue(metadata.Id, out var value);
			if (replay)
			{
				if (value.Kind != metadata.Kind || value.Group != metadata.Group)
				{
					throw new ProtocolError(409, "Existing artifact ID belongs to a different kind/group; revision refused.");
				}
				if (value.Sha256 != text || value.Length != length)
				{
					string text2 = ObjectPath(text);
					if (!File.Exists(text2))
					{
						if (UsedBytes(objects) + length > options.MaximumStoreBytes)
						{
							throw new ProtocolError(507, "Local asset-store quota reached; the prior career revision was preserved.");
						}
						File.Move(temporary, text2);
					}
					else
					{
						if (new FileInfo(text2).Length != length || RepairJson.HashFile(text2) != text)
						{
							throw new ProtocolError(500, "Existing content-addressed revision is corrupt.");
						}
						File.Delete(temporary);
					}
					List<string> list = new List<string>(value.UploadPaths);
					if (!list.Contains(uploadPath))
					{
						list.Add(uploadPath);
					}
					AssetRecord assetRecord = new AssetRecord
					{
						Id = value.Id,
						Kind = value.Kind,
						Group = value.Group,
						Sha256 = text,
						Length = length,
						Validation = validation + "; supersedes=" + value.Sha256,
						CreatedUtc = DateTime.UtcNow.ToString("O"),
						UploadPaths = list
					};
					RepairJson.AtomicWrite(ManifestPath(assetRecord.Id), RepairJson.Encode(assetRecord));
					records[assetRecord.Id] = assetRecord;
					RefreshGroup(assetRecord.Group);
					replay = false;
					revised = true;
					return text;
				}
				using (OpenVerified(value))
				{
				}
				if (!value.UploadPaths.Contains(uploadPath))
				{
					AssetRecord assetRecord2 = new AssetRecord
					{
						Id = value.Id,
						Kind = value.Kind,
						Group = value.Group,
						Sha256 = value.Sha256,
						Length = value.Length,
						Validation = value.Validation,
						CreatedUtc = value.CreatedUtc,
						UploadPaths = new List<string>(value.UploadPaths)
					};
					assetRecord2.UploadPaths.Add(uploadPath);
					RepairJson.AtomicWrite(ManifestPath(assetRecord2.Id), RepairJson.Encode(assetRecord2));
					records[assetRecord2.Id] = assetRecord2;
				}
				File.Delete(temporary);
				return text;
			}
			string text3 = ObjectPath(text);
			if (!File.Exists(text3))
			{
				if (UsedBytes(objects) + length > options.MaximumStoreBytes)
				{
					throw new ProtocolError(507, "Local asset-store quota reached; no existing career assets were deleted.");
				}
				File.Move(temporary, text3);
			}
			else
			{
				if (new FileInfo(text3).Length != length || RepairJson.HashFile(text3) != text)
				{
					throw new ProtocolError(500, "Existing content-addressed object is corrupt.");
				}
				File.Delete(temporary);
			}
			AssetRecord assetRecord3 = new AssetRecord
			{
				Id = metadata.Id,
				Kind = metadata.Kind,
				Group = metadata.Group,
				Sha256 = text,
				Length = length,
				Validation = validation,
				CreatedUtc = DateTime.UtcNow.ToString("O"),
				UploadPaths = new List<string> { uploadPath }
			};
			RepairJson.AtomicWrite(ManifestPath(assetRecord3.Id), RepairJson.Encode(assetRecord3));
			records.Add(assetRecord3.Id, assetRecord3);
			RefreshGroup(assetRecord3.Group);
			return text;
		}
	}

	private void RefreshGroup(string group)
	{
		HashSet<string> hashSet = new HashSet<string>(from r in records.Values
			where r.Group == @group
			select r.Kind, StringComparer.Ordinal);
		if (hashSet.Contains("RenderData") && hashSet.Contains("Logo1Data") && hashSet.Contains("Logo2Data"))
		{
			completeGroups.Add(group);
		}
	}

	public void Quarantine(string temporary, string reason)
	{
		if (!File.Exists(temporary))
		{
			return;
		}
		lock (sync)
		{
			long length = new FileInfo(temporary).Length;
			if (UsedBytes(quarantine) + length > options.MaximumQuarantineBytes)
			{
				File.Delete(temporary);
				return;
			}
			string text = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N");
			string destFileName = Path.Combine(quarantine, text + ".bin");
			File.Move(temporary, destFileName);
			RepairJson.AtomicWrite(Path.Combine(quarantine, text + ".reason.json"), RepairJson.Encode(reason));
		}
	}

	public void Dispose()
	{
		ownership.Dispose();
	}
}
