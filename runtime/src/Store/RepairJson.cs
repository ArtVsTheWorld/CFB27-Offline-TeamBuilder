using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace OfflineTeamBuilder.Repair;

internal static class RepairJson
{
	public static byte[] Encode<T>(T value)
	{
		using MemoryStream memoryStream = new MemoryStream();
		new DataContractJsonSerializer(typeof(T)).WriteObject(memoryStream, value);
		return memoryStream.ToArray();
	}

	public static T Decode<T>(byte[] bytes)
	{
		using MemoryStream stream = new MemoryStream(bytes);
		return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
	}

	public static string Hash(byte[] bytes)
	{
		using SHA256 sHA = SHA256.Create();
		return Hex(sHA.ComputeHash(bytes));
	}

	public static string HashFile(string file)
	{
		using SHA256 sHA = SHA256.Create();
		using FileStream inputStream = File.OpenRead(file);
		return Hex(sHA.ComputeHash(inputStream));
	}

	public static string Hex(byte[] bytes)
	{
		StringBuilder stringBuilder = new StringBuilder(bytes.Length * 2);
		foreach (byte b in bytes)
		{
			stringBuilder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	public static bool IsHash(string value)
	{
		if (value != null && value.Length == 64)
		{
			return value.All((char c) => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
		}
		return false;
	}

	internal static string AtomicTemporaryName()
	{
		return ".tb-" + Guid.NewGuid().ToString("N").Substring(0, 16) + ".tmp";
	}

	public static void AtomicWrite(string path, byte[] data)
	{
		string fullPath = Path.GetFullPath(path);
		string directoryName = Path.GetDirectoryName(fullPath);
		Directory.CreateDirectory(directoryName);
		string text = null;
		FileStream fileStream = null;
		for (int i = 0; i < 8; i++)
		{
			string text2 = Path.Combine(directoryName, AtomicTemporaryName());
			try
			{
				fileStream = new FileStream(text2, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				text = text2;
			}
			catch (IOException)
			{
				if (!File.Exists(text2))
				{
					throw;
				}
				continue;
			}
			break;
		}
		if (fileStream == null)
		{
			throw new IOException("Could not allocate a unique atomic-write temporary file.");
		}
		try
		{
			using (fileStream)
			{
				fileStream.Write(data, 0, data.Length);
				fileStream.Flush(flushToDisk: true);
			}
			if (File.Exists(fullPath))
			{
				File.Replace(text, fullPath, null);
			}
			else
			{
				File.Move(text, fullPath);
			}
		}
		finally
		{
			if (text != null && File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static MetadataRecord ParseMetadata(byte[] bytes, int limit)
	{
		if (bytes.Length == 0 || bytes.Length > limit)
		{
			throw new ProtocolError(413, "Metadata body size is invalid.");
		}
		XmlDictionaryReaderQuotas quotas = new XmlDictionaryReaderQuotas
		{
			MaxDepth = 32,
			MaxStringContentLength = limit,
			MaxArrayLength = limit,
			MaxBytesPerRead = 4096,
			MaxNameTableCharCount = limit
		};
		XmlDocument xmlDocument = new XmlDocument
		{
			XmlResolver = null
		};
		try
		{
			using XmlDictionaryReader reader = JsonReaderWriterFactory.CreateJsonReader(bytes, quotas);
			xmlDocument.Load(reader);
		}
		catch (Exception ex) when (ex is XmlException || ex is SerializationException || ex is ArgumentException)
		{
			throw new ProtocolError(400, "Metadata is not valid JSON.");
		}
		XmlElement documentElement = xmlDocument.DocumentElement;
		if (documentElement == null || documentElement.GetAttribute("type") != "object")
		{
			throw new ProtocolError(400, "Metadata must be a JSON object.");
		}
		List<XmlElement> list = (from e in documentElement.ChildNodes.OfType<XmlElement>()
			where e.LocalName == "id"
			select e).ToList();
		List<XmlElement> list2 = (from e in documentElement.ChildNodes.OfType<XmlElement>()
			where e.LocalName == "assetCategory"
			select e).ToList();
		if (list.Count != 1 || list2.Count != 1 || list[0].GetAttribute("type") != "string" || list2[0].GetAttribute("type") != "string" || list2[0].InnerText != "ASSET_CATEGORY_FILE")
		{
			throw new ProtocolError(400, "Expected one string id and ASSET_CATEGORY_FILE.");
		}
		if (!xmlDocument.SelectNodes("//*[not(*)]").OfType<XmlElement>().Any((XmlElement e) => e.GetAttribute("type") == "string" && e.InnerText == "TEAMBUILDER_TEAM_ART"))
		{
			throw new ProtocolError(400, "Not a TeamBuilder team-art transaction.");
		}
		string innerText = list[0].InnerText;
		Identify(innerText, out var kind, out var group);
		return new MetadataRecord
		{
			Id = innerText,
			Kind = kind,
			Group = group,
			OriginalJson = bytes
		};
	}

	public static void Identify(string id, out string kind, out string group)
	{
		if (string.IsNullOrWhiteSpace(id) || id.Length > 512 || id.Any((char c) => char.IsControl(c) || c == '/' || c == '\\'))
		{
			throw new ProtocolError(400, "Invalid native asset identifier.");
		}
		kind = null;
		group = null;
		string[] array = new string[3] { "RenderData", "Logo1Data", "Logo2Data" };
		foreach (string text in array)
		{
			string text2 = "-TeamBuilder" + text + "-";
			int num2 = id.IndexOf(text2, StringComparison.Ordinal);
			if (num2 > 0 && num2 + text2.Length < id.Length)
			{
				if (kind != null || id.IndexOf(text2, num2 + text2.Length, StringComparison.Ordinal) >= 0)
				{
					throw new ProtocolError(400, "Ambiguous asset identifier.");
				}
				kind = text;
				group = id.Substring(0, num2) + "-TeamBuilderGroup-" + id.Substring(num2 + text2.Length);
			}
		}
		if (kind == null)
		{
			throw new ProtocolError(400, "Unknown TeamBuilder artifact kind.");
		}
	}
}
