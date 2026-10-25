using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OfflineTeamBuilder.Repair;

internal static class ReloadDiagnostics
{
	private static readonly Regex ArtifactId = new Regex("^\\d{1,20}-TeamBuilder(?:RenderData|Logo1Data|Logo2Data)-\\d{1,20}$", RegexOptions.CultureInvariant);

	private static readonly Regex SimpleRoute = new Regex("^[A-Za-z][A-Za-z0-9_.-]{0,31}$", RegexOptions.CultureInvariant);

	private static readonly Regex NumericPart = new Regex("^-?\\d{1,20}$", RegexOptions.CultureInvariant);

	private static readonly HashSet<string> SensitiveNames = new HashSet<string>(new string[16]
	{
		"token", "access_token", "refresh_token", "authorization", "auth", "password", "key", "apikey", "api_key", "signature",
		"sig", "credential", "credentials", "ticket", "session", "sessionid"
	}, StringComparer.OrdinalIgnoreCase);

	private static readonly object Sync = new object();

	internal static string ShortSessionDirectory(string localAppData, string sessionName)
	{
		if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathRooted(localAppData))
		{
			throw new IOException("Local application-data directory is unavailable.");
		}
		return Path.Combine(localAppData, "OfflineTeamBuilder", "diagnostics", "sessions", sessionName);
	}

	internal static string SafeUrl(string input)
	{
		if (string.IsNullOrEmpty(input))
		{
			return "<empty or unreadable>";
		}
		if (input.Length > 4096 || input.Any(char.IsControl))
		{
			return "<invalid or oversized URL sample>";
		}
		string text = "";
		string text2 = input;
		if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			if (!Uri.TryCreate(input, UriKind.Absolute, out var result))
			{
				return "<unparseable URL sample>";
			}
			text = result.Scheme + "://" + result.Host + (result.IsDefaultPort ? "" : (":" + result.Port.ToString(CultureInfo.InvariantCulture)));
			text2 = result.PathAndQuery;
		}
		else if (!input.StartsWith("/", StringComparison.Ordinal))
		{
			return "<non-URL sample>";
		}
		int num = text2.IndexOf('#');
		if (num >= 0)
		{
			text2 = text2.Substring(0, num);
		}
		int num2 = text2.IndexOf('?');
		string obj = ((num2 >= 0) ? text2.Substring(0, num2) : text2);
		string text3 = ((num2 >= 0) ? text2.Substring(num2 + 1) : "");
		string[] array = obj.Split('/');
		List<string> list = new List<string>();
		bool flag = false;
		string[] array2 = array;
		foreach (string stringToUnescape in array2)
		{
			string text4;
			try
			{
				text4 = Uri.UnescapeDataString(stringToUnescape);
			}
			catch
			{
				text4 = "";
			}
			if (flag)
			{
				list.Add("<redacted>");
				flag = false;
			}
			else if (text4.Length == 0)
			{
				list.Add("");
			}
			else if (SensitiveNames.Contains(text4))
			{
				list.Add(text4);
				flag = true;
			}
			else if (ArtifactId.IsMatch(text4) || NumericPart.IsMatch(text4) || SimpleRoute.IsMatch(text4))
			{
				list.Add(Uri.EscapeDataString(text4));
			}
			else
			{
				list.Add("<segment-sha256-" + RepairJson.Hash(Encoding.UTF8.GetBytes(text4)).Substring(0, 12) + ">");
			}
		}
		StringBuilder stringBuilder = new StringBuilder(text + string.Join("/", list));
		if (text3.Length != 0)
		{
			stringBuilder.Append('?');
			bool flag2 = true;
			int num3 = 0;
			array2 = text3.Split('&');
			foreach (string text5 in array2)
			{
				if (++num3 > 32)
				{
					stringBuilder.Append("&<additional-parameters>");
					break;
				}
				int num4 = text5.IndexOf('=');
				string text6 = ((num4 < 0) ? text5 : text5.Substring(0, num4));
				string text7 = ((num4 < 0) ? "" : text5.Substring(num4 + 1));
				try
				{
					text6 = Uri.UnescapeDataString(text6.Replace('+', ' '));
					text7 = Uri.UnescapeDataString(text7.Replace('+', ' '));
				}
				catch
				{
					text6 = "invalid";
					text7 = "";
				}
				if (!flag2)
				{
					stringBuilder.Append('&');
				}
				flag2 = false;
				stringBuilder.Append(SimpleRoute.IsMatch(text6) ? Uri.EscapeDataString(text6) : "<parameter>");
				stringBuilder.Append('=');
				stringBuilder.Append((!SensitiveNames.Contains(text6) && ArtifactId.IsMatch(text7)) ? Uri.EscapeDataString(text7) : "<redacted>");
			}
		}
		return stringBuilder.ToString();
	}

	internal static void Append(string directory, ReloadProbeRecord record)
	{
		record.Utc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
		string text = Path.Combine(directory, "reload-trace.jsonl");
		lock (Sync)
		{
			Directory.CreateDirectory(directory);
			if (File.Exists(text) && new FileInfo(text).Length > 16777216)
			{
				string text2 = Path.Combine(directory, "reload-trace.previous.jsonl");
				if (File.Exists(text2))
				{
					File.Delete(text2);
				}
				File.Move(text, text2);
			}
			File.AppendAllText(text, Encoding.UTF8.GetString(RepairJson.Encode(record)) + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
	}
}
