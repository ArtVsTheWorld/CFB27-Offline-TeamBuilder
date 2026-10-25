using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OfflineTeamBuilder.Repair;

public static class CareerUrlBinding
{
	public const int LocalPort = 27875;

	private const int MaxPayload = 67108864;

	private const int SlotLength = 501;

	private static readonly byte[] TableName = Encoding.ASCII.GetBytes("TeamBuilderData\0");

	private static readonly string[] FieldNames = new string[4] { "TertiaryLogoURL", "SecondaryLogoURL", "PrimaryLogoURL", "FrostbiteBinaryURL" };

	private static readonly string[] Kinds = new string[4] { null, "Logo2Data", "Logo1Data", "RenderData" };

	internal static uint BE(byte[] b, int offset)
	{
		Bounds(b, offset, 4);
		return (uint)((b[offset] << 24) | (b[offset + 1] << 16) | (b[offset + 2] << 8) | b[offset + 3]);
	}

	private static uint LE(byte[] b, int offset)
	{
		Bounds(b, offset, 4);
		return (uint)(b[offset] | (b[offset + 1] << 8) | (b[offset + 2] << 16) | (b[offset + 3] << 24));
	}

	private static void PutLE(byte[] b, int offset, uint value)
	{
		for (int i = 0; i < 4; i++)
		{
			b[offset + i] = (byte)(value >> 8 * i);
		}
	}

	private static void Bounds(byte[] b, int offset, int size)
	{
		if (offset < 0 || size < 0 || offset > b.Length - size)
		{
			throw new InvalidDataException("Career/table bounds are invalid.");
		}
	}

	private static bool Match(byte[] data, int offset, byte[] pattern)
	{
		if (offset < 0 || offset > data.Length - pattern.Length)
		{
			return false;
		}
		for (int i = 0; i < pattern.Length; i++)
		{
			if (data[offset + i] != pattern[i])
			{
				return false;
			}
		}
		return true;
	}

	private static uint Adler(byte[] b)
	{
		uint num = 1u;
		uint num2 = 0u;
		foreach (byte b2 in b)
		{
			num = (num + b2) % 65521;
			num2 = (num2 + num) % 65521;
		}
		return (num2 << 16) | num;
	}

	internal static byte[] InflateCareer(byte[] file, out int compressedLength)
	{
		if (file.Length < 88 || file.Length > 67108864 || !Match(file, 0, Encoding.ASCII.GetBytes("FBCHUNKS")) || file[8] != 1 || file[9] != 0 || LE(file, 10) != 64 || LE(file, 14) != file.Length - 82)
		{
			throw new InvalidDataException("Unsupported career FBCHUNKS envelope; input left unchanged.");
		}
		if (!Match(file, 34, Encoding.ASCII.GetBytes("College-27-")))
		{
			throw new InvalidDataException("Not a recognized College Football 27 career.");
		}
		if (LE(file, 62) != 833)
		{
			throw new InvalidDataException("Only the observed career schema 833 is supported by this copy binder.");
		}
		uint num = LE(file, 74);
		if (LE(file, 78) != 0 || num < 6 || num > file.Length - 82)
		{
			throw new InvalidDataException("Invalid declared compressed career length.");
		}
		compressedLength = (int)num;
		int num2 = 82;
		if ((file[num2] & 0xF) != 8 || file[num2] >> 4 > 7 || ((file[num2] << 8) + file[num2 + 1]) % 31 != 0 || (file[num2 + 1] & 0x20) != 0)
		{
			throw new InvalidDataException("Unsupported zlib wrapper.");
		}
		byte[] array2;
		using (MemoryStream stream = new MemoryStream(file, num2 + 2, compressedLength - 6, writable: false))
		{
			using DeflateStream deflateStream = new DeflateStream(stream, CompressionMode.Decompress);
			using MemoryStream memoryStream = new MemoryStream();
			byte[] array = new byte[65536];
			int num3;
			while ((num3 = deflateStream.Read(array, 0, array.Length)) > 0)
			{
				if (memoryStream.Length + num3 > 67108864)
				{
					throw new InvalidDataException("Inflated career exceeds limit.");
				}
				memoryStream.Write(array, 0, num3);
			}
			array2 = memoryStream.ToArray();
		}
		if (!Match(array2, 0, Encoding.ASCII.GetBytes("FrTk")) || array2.Length < 128 || Adler(array2) != BE(file, num2 + compressedLength - 4))
		{
			throw new InvalidDataException("Career payload or zlib checksum is invalid.");
		}
		return array2;
	}

	private static byte[] Repack(byte[] original, byte[] payload)
	{
		byte[] array;
		using (MemoryStream memoryStream = new MemoryStream())
		{
			memoryStream.WriteByte(120);
			memoryStream.WriteByte(156);
			using (DeflateStream deflateStream = new DeflateStream(memoryStream, CompressionLevel.Optimal, leaveOpen: true))
			{
				deflateStream.Write(payload, 0, payload.Length);
			}
			uint num = Adler(payload);
			memoryStream.WriteByte((byte)(num >> 24));
			memoryStream.WriteByte((byte)(num >> 16));
			memoryStream.WriteByte((byte)(num >> 8));
			memoryStream.WriteByte((byte)num);
			array = memoryStream.ToArray();
		}
		int num2 = (int)LE(original, 74);
		if (array.Length > original.Length - 82)
		{
			throw new InvalidDataException("Rebound stream exceeds the existing allocated body.");
		}
		for (int i = 82 + num2; i < 82 + array.Length; i++)
		{
			if (original[i] != 0)
			{
				throw new InvalidDataException("Rebound stream would overwrite nonzero opaque trailing content; no output produced.");
			}
		}
		byte[] array2 = (byte[])original.Clone();
		PutLE(array2, 74, (uint)array.Length);
		Buffer.BlockCopy(array, 0, array2, 82, array.Length);
		return array2;
	}

	internal static string LocatorId(string locator)
	{
		string text = "http://127.0.0.1:" + 27875 + "/";
		string[] array = (locator.StartsWith(text, StringComparison.Ordinal) ? locator.Substring(text.Length) : locator).Split('/');
		if (array.Length != 3 || array[0] != "0" || array[1] != array[2])
		{
			throw new InvalidDataException("Unsupported saved TeamBuilder locator: expected 0/<id>/<same-id>.");
		}
		if (array[1].Any((char c) => c > '\u007f'))
		{
			throw new InvalidDataException("Non-ASCII native IDs are not supported by this observed-format binder.");
		}
		RepairJson.Identify(array[1], out var _, out var _);
		return array[1];
	}

	internal static byte[] BindBuffer(byte[] input, Func<string, string, bool> hasAsset, out CareerBindingReport report)
	{
		int compressedLength;
		byte[] array = InflateCareer(input, out compressedLength);
		byte[] array2 = (byte[])array.Clone();
		report = new CareerBindingReport
		{
			SourceSha256 = RepairJson.Hash(input),
			PayloadLength = array.Length,
			SchemaMajor = 833
		};
		List<int> list = new List<int>();
		for (int i = 128; i <= array.Length - 780; i++)
		{
			if (array[i] == 84 && Match(array, i, TableName) && BE(array, i + 132) == 3844932060u && Match(array, i + 148, Encoding.ASCII.GetBytes("SPBF")) && BE(array, i + 164) == 20 && Match(array, i + 168, Encoding.ASCII.GetBytes("TeamBuilderDataStore")) && Match(array, i + 220, Encoding.ASCII.GetBytes("BSFT")))
			{
				list.Add(i);
			}
		}
		if (list.Count != 1)
		{
			throw new InvalidDataException("Expected exactly one verified TeamBuilderData table; no edits made.");
		}
		int num = list[0];
		uint num2 = BE(array, num + 196);
		uint num3 = BE(array, num + 236);
		if (num2 == 0 || num2 > 256 || num2 != num3 || BE(array, num + 232) != 4 || BE(array, num + 240) != 4 || BE(array, num + 212) != 0 || BE(array, num + 216) != 3844932060u || BE(array, num + 204) != BE(array, num + 224) || BE(array, num + 208) != num2 * 4 * 501 || BE(array, num + 252) != 96 || BE(array, num + 256) != 64 || BE(array, num + 260) != 32 || BE(array, num + 264) != 0)
		{
			throw new InvalidDataException("TeamBuilderData layout differs from the verified four-string schema; no edits made.");
		}
		int num4 = num + 268;
		int num5;
		int num6;
		HashSet<int> hashSet;
		checked
		{
			num5 = num4 + (int)num2 * 16;
			num6 = (int)num2 * 4 * 501;
			Bounds(array, num5, num6);
			hashSet = new HashSet<int>();
		}
		uint num7 = BE(array, num + 248);
		while (num7 < num2)
		{
			if (!hashSet.Add((int)num7))
			{
				throw new InvalidDataException("Invalid/cyclic empty-row chain.");
			}
			int num8 = num4 + (int)(num7 * 16);
			if (BE(array, num8 + 4) != 0 || BE(array, num8 + 8) != 0 || BE(array, num8 + 12) != 0)
			{
				throw new InvalidDataException("Unexpected empty-row layout.");
			}
			num7 = BE(array, num8);
		}
		if (num7 != num2)
		{
			throw new InvalidDataException("Empty-row chain exceeds table capacity.");
		}
		Dictionary<int, string> dictionary = new Dictionary<int, string>();
		for (int j = 0; j < num2; j++)
		{
			if (hashSet.Contains(j))
			{
				continue;
			}
			string text = null;
			for (int k = 0; k < 4; k++)
			{
				uint num9 = BE(array, num4 + j * 16 + k * 4);
				if (num9 % 501 != 0 || num9 > num6 - 501)
				{
					throw new InvalidDataException("String pointer outside the verified fixed-slot table.");
				}
				int num10 = num5 + (int)num9;
				int l;
				for (l = num10; l < num10 + 501 && array[l] != 0; l++)
				{
				}
				if (l == num10 + 501)
				{
					throw new InvalidDataException("Unterminated URL field.");
				}
				for (int m = l; m < num10 + 501; m++)
				{
					if (array[m] != 0)
					{
						throw new InvalidDataException("Nonzero string-slot padding is not supported.");
					}
				}
				string text2 = Encoding.UTF8.GetString(array, num10, l - num10);
				if (text2.Length != 0)
				{
					if (k == 0)
					{
						throw new InvalidDataException("A nonempty tertiary logo requires an additional verified artifact kind.");
					}
					string text3 = LocatorId(text2);
					RepairJson.Identify(text3, out var kind, out var group);
					if (kind != Kinds[k] || (text != null && group != text))
					{
						throw new InvalidDataException("Mismatched artifact kind/group in career row.");
					}
					text = group;
					if (!hasAsset(text3, kind))
					{
						throw new InvalidDataException("No verified cached artifact for " + text3 + ".");
					}
					string text4 = "http://127.0.0.1:" + 27875 + "/0/" + text3 + "/" + text3;
					byte[] bytes = Encoding.ASCII.GetBytes(text4);
					if (bytes.Length >= 501)
					{
						throw new InvalidDataException("Local URL exceeds the existing string slot.");
					}
					if (dictionary.ContainsKey(num10) && dictionary[num10] != text4)
					{
						throw new InvalidDataException("Conflicting shared string-slot references.");
					}
					dictionary[num10] = text4;
					if (!(text2 == text4))
					{
						Array.Clear(array2, num10, 501);
						Buffer.BlockCopy(bytes, 0, array2, num10, bytes.Length);
						report.Changes.Add(new CareerUrlChange
						{
							Row = j,
							Field = FieldNames[k],
							ArtifactId = text3,
							Before = text2,
							After = text4,
							PayloadOffset = num10
						});
					}
				}
			}
		}
		var source = report.Changes.Select((CareerUrlChange c) => new
		{
			Start = c.PayloadOffset,
			End = c.PayloadOffset + 501
		}).ToArray();
		int i2;
		for (i2 = 0; i2 < array.Length; i2++)
		{
			if (array[i2] != array2[i2] && !source.Any(x => i2 >= x.Start && i2 < x.End))
			{
				throw new InvalidDataException("Unexpected payload edit outside URL slots.");
			}
		}
		report.AllOtherPayloadBytesUnchanged = true;
		byte[] array3 = ((report.Changes.Count == 0) ? ((byte[])input.Clone()) : Repack(input, array2));
		if (!InflateCareer(array3, out var _).SequenceEqual(array2))
		{
			throw new InvalidDataException("Repacked career failed round-trip verification.");
		}
		report.OutputSha256 = RepairJson.Hash(array3);
		return array3;
	}

	public static CareerBindingReport CreateLocalCopy(string source, string destination, string storeDirectory)
	{
		source = Path.GetFullPath(source);
		destination = Path.GetFullPath(destination);
		storeDirectory = Path.GetFullPath(storeDirectory);
		if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
		{
			throw new IOException("Only a new separate career copy is permitted; existing files are never overwritten.");
		}
		if (!Path.GetFileName(source).StartsWith("RTG-", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(source).StartsWith("DYNASTY-", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("Choose an RTG- or DYNASTY- career file, not a downloaded team or another save type.");
		}
		Dictionary<string, bool> cached = new Dictionary<string, bool>(StringComparer.Ordinal);
		Func<string, string, bool> hasAsset = delegate(string id, string kind)
		{
			if (cached.TryGetValue(id, out var value))
			{
				return value;
			}
			string path = Path.Combine(storeDirectory, "manifests", RepairJson.Hash(Encoding.UTF8.GetBytes(id)) + ".json");
			if (!File.Exists(path))
			{
				return false;
			}
			AssetRecord assetRecord = RepairJson.Decode<AssetRecord>(File.ReadAllBytes(path));
			if (assetRecord.Id != id || assetRecord.Kind != kind || !RepairJson.IsHash(assetRecord.Sha256))
			{
				return false;
			}
			string text2 = Path.Combine(storeDirectory, "objects", assetRecord.Sha256 + ".blob");
			value = File.Exists(text2) && new FileInfo(text2).Length == assetRecord.Length && RepairJson.HashFile(text2) == assetRecord.Sha256;
			cached[id] = value;
			return value;
		};
		CareerBindingReport report;
		byte[] array = BindBuffer(File.ReadAllBytes(source), hasAsset, out report);
		if (RepairJson.HashFile(source) != report.SourceSha256)
		{
			throw new IOException("Source changed during preparation; no output published.");
		}
		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		string text = Path.Combine(Path.GetDirectoryName(destination), RepairJson.AtomicTemporaryName());
		try
		{
			using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				fileStream.Write(array, 0, array.Length);
				fileStream.Flush(flushToDisk: true);
			}
			File.Move(text, destination);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
		try
		{
			RepairJson.AtomicWrite(Path.Combine(storeDirectory, "binding-reports", report.OutputSha256 + ".json"), RepairJson.Encode(report));
		}
		catch
		{
		}
		return report;
	}
}
