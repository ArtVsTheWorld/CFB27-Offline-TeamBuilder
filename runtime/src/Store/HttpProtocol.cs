using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace OfflineTeamBuilder.Repair;

internal sealed class HttpProtocol
{
	private readonly NetworkStream stream;

	private readonly RepairOptions options;

	private readonly Stopwatch clock = Stopwatch.StartNew();

	public HttpProtocol(NetworkStream stream, RepairOptions options)
	{
		this.stream = stream;
		this.options = options;
	}

	private void Timeout()
	{
		long num = options.RequestDeadlineMilliseconds - clock.ElapsedMilliseconds;
		if (num <= 0)
		{
			throw new ProtocolError(408, "Request deadline exceeded.");
		}
		stream.ReadTimeout = (int)Math.Min(num, options.IdleTimeoutMilliseconds);
		stream.WriteTimeout = options.IdleTimeoutMilliseconds;
	}

	private string Line(int max)
	{
		using MemoryStream memoryStream = new MemoryStream();
		while (true)
		{
			Timeout();
			int num = stream.ReadByte();
			if (num < 0)
			{
				throw new ProtocolError(400, "Connection ended before request framing was complete.");
			}
			if (num == 13)
			{
				Timeout();
				if (stream.ReadByte() != 10)
				{
					throw new ProtocolError(400, "Expected CRLF.");
				}
				return Encoding.ASCII.GetString(memoryStream.ToArray());
			}
			if (num == 10 || num == 0 || num > 127 || memoryStream.Length >= max)
			{
				break;
			}
			memoryStream.WriteByte((byte)num);
		}
		throw new ProtocolError(400, "Invalid or oversized HTTP line.");
	}

	public HttpRequest ReadHeaders(int port)
	{
		string text = Line(8192);
		string[] array = text.Split(' ');
		if (array.Length != 3 || (array[2] != "HTTP/1.1" && array[2] != "HTTP/1.0"))
		{
			throw new ProtocolError(400, "Invalid HTTP request line.");
		}
		HttpRequest httpRequest = new HttpRequest
		{
			Method = array[0]
		};
		string text2 = array[1];
		if (text2.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text2.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
		{
			if (!Uri.TryCreate(text2, UriKind.Absolute, out var result) || !LocalHost(result.Host) || result.Port != port)
			{
				throw new ProtocolError(400, "Absolute request target must point to this loopback listener.");
			}
			text2 = result.PathAndQuery;
		}
		httpRequest.TargetForDiagnostics = text2;
		int num = text2.IndexOf('?');
		httpRequest.Path = ((num >= 0) ? text2.Substring(0, num) : text2);
		if (!httpRequest.Path.StartsWith("/", StringComparison.Ordinal) || httpRequest.Path.Contains("#") || httpRequest.Path.Contains("\\"))
		{
			throw new ProtocolError(400, "Invalid request path.");
		}
		int num2 = text.Length + 2;
		int num3 = 0;
		while (true)
		{
			string text3 = Line(8192);
			num2 += text3.Length + 2;
			if (num2 > 65536 || ++num3 > 128)
			{
				throw new ProtocolError(431, "Request headers exceed limits.");
			}
			if (text3.Length == 0)
			{
				break;
			}
			int num4 = text3.IndexOf(':');
			if (num4 <= 0 || char.IsWhiteSpace(text3[0]))
			{
				throw new ProtocolError(400, "Invalid header field.");
			}
			string text4 = text3.Substring(0, num4);
			string text5 = text3.Substring(num4 + 1).Trim();
			string text6 = text4;
			foreach (char c in text6)
			{
				if (!char.IsLetterOrDigit(c) && "!#$%&'*+-.^_`|~".IndexOf(c) < 0)
				{
					throw new ProtocolError(400, "Invalid header name.");
				}
			}
			if (httpRequest.Headers.ContainsKey(text4))
			{
				if (text4.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) || text4.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) || text4.Equals("Host", StringComparison.OrdinalIgnoreCase))
				{
					throw new ProtocolError(400, "Duplicate framing/Host header.");
				}
				Dictionary<string, string> headers = httpRequest.Headers;
				text6 = text4;
				headers[text6] = headers[text6] + "," + text5;
			}
			else
			{
				httpRequest.Headers.Add(text4, text5);
			}
		}
		if (httpRequest.Headers.TryGetValue("Host", out var value) && (!Uri.TryCreate("http://" + value, UriKind.Absolute, out var result2) || !LocalHost(result2.Host) || (result2.Port != port && !result2.IsDefaultPort)))
		{
			throw new ProtocolError(400, "Host must be loopback.");
		}
		if (httpRequest.Headers.ContainsKey("Origin"))
		{
			throw new ProtocolError(403, "Browser-origin requests are not accepted by this local game service.");
		}
		string value2;
		bool num5 = httpRequest.Headers.TryGetValue("Content-Length", out value2);
		string value3;
		bool flag = httpRequest.Headers.TryGetValue("Transfer-Encoding", out value3);
		if (num5 && flag)
		{
			throw new ProtocolError(400, "Ambiguous Content-Length and Transfer-Encoding.");
		}
		if (flag)
		{
			if (!value3.Trim().Equals("chunked", StringComparison.OrdinalIgnoreCase))
			{
				throw new ProtocolError(501, "Unsupported transfer encoding.");
			}
			httpRequest.Chunked = true;
		}
		if (num5)
		{
			if (!long.TryParse(value2, NumberStyles.None, CultureInfo.InvariantCulture, out var result3))
			{
				throw new ProtocolError(400, "Invalid Content-Length.");
			}
			if (result3 > options.MaximumUploadBytes)
			{
				throw new ProtocolError(413, "Upload exceeds configured size limit.");
			}
			httpRequest.Length = result3;
		}
		int num6;
		if (!(httpRequest.Method == "POST"))
		{
			num6 = ((httpRequest.Method == "PUT") ? 1 : 0);
			if (num6 == 0)
			{
				goto IL_0433;
			}
		}
		else
		{
			num6 = 1;
		}
		if (!httpRequest.Length.HasValue && !httpRequest.Chunked)
		{
			throw new ProtocolError(411, "A valid Content-Length or chunked body is required.");
		}
		goto IL_0433;
		IL_0433:
		if (num6 == 0 && (httpRequest.Chunked || httpRequest.Length.GetValueOrDefault() != 0L))
		{
			throw new ProtocolError(400, "Read requests cannot contain bodies.");
		}
		if (httpRequest.Headers.TryGetValue("Expect", out var value4))
		{
			if (!value4.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
			{
				throw new ProtocolError(417, "Unsupported expectation.");
			}
			httpRequest.ContinueExpected = true;
		}
		return httpRequest;
	}

	private static bool LocalHost(string host)
	{
		if (!host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase) && !(host == "[::1]"))
		{
			return host == "::1";
		}
		return true;
	}

	public void Continue(HttpRequest r)
	{
		if (r.ContinueExpected)
		{
			byte[] bytes = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
			Timeout();
			stream.Write(bytes, 0, bytes.Length);
			stream.Flush();
		}
	}

	private void Copy(Stream target, long count)
	{
		byte[] array = new byte[65536];
		while (count > 0)
		{
			Timeout();
			int num = stream.Read(array, 0, (int)Math.Min(array.Length, count));
			if (num <= 0)
			{
				throw new ProtocolError(400, "Truncated upload body.");
			}
			target.Write(array, 0, num);
			count -= num;
		}
	}

	public long ReadBody(HttpRequest r, Stream destination, long maximum)
	{
		if (r.Length.HasValue)
		{
			if (r.Length.Value > maximum)
			{
				throw new ProtocolError(413, "Body exceeds endpoint limit.");
			}
			Copy(destination, r.Length.Value);
			return r.Length.Value;
		}
		if (!r.Chunked)
		{
			return 0L;
		}
		long num = 0L;
		do
		{
			string text = Line(1024);
			string text2 = text.Split(';')[0];
			if (text2.Length == 0 || text2.Length > 16 || !long.TryParse(text2, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var result) || result < 0)
			{
				throw new ProtocolError(400, "Invalid chunk length.");
			}
			if (result == 0L)
			{
				int num2 = 0;
				while ((text = Line(8192)).Length != 0)
				{
					num2 += text.Length;
					if (num2 > 16384 || text.IndexOf(':') <= 0 || text.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Transfer-Encoding:", StringComparison.OrdinalIgnoreCase))
					{
						throw new ProtocolError(400, "Invalid chunk trailers.");
					}
				}
				return num;
			}
			if (result > maximum - num)
			{
				throw new ProtocolError(413, "Decoded chunked body exceeds limit.");
			}
			Copy(destination, result);
			num += result;
		}
		while (Line(0).Length == 0);
		throw new ProtocolError(400, "Invalid chunk terminator.");
	}

	public void Reply(int status, byte[] body, string contentType = "application/json", bool head = false, Dictionary<string, string> extra = null)
	{
		Header(status, body.LongLength, contentType, extra);
		if (!head && body.Length != 0)
		{
			Timeout();
			stream.Write(body, 0, body.Length);
		}
		stream.Flush();
	}

	private void Header(int status, long length, string type, Dictionary<string, string> extra)
	{
		StringBuilder stringBuilder = new StringBuilder("HTTP/1.1 " + status.ToString(CultureInfo.InvariantCulture) + " " + Reason(status) + "\r\n");
		stringBuilder.Append("Content-Type: ").Append(type).Append("\r\nContent-Length: ")
			.Append(length.ToString(CultureInfo.InvariantCulture))
			.Append("\r\nConnection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n");
		if (extra != null)
		{
			foreach (KeyValuePair<string, string> item in extra)
			{
				stringBuilder.Append(item.Key).Append(": ").Append(item.Value)
					.Append("\r\n");
			}
		}
		stringBuilder.Append("\r\n");
		byte[] bytes = Encoding.ASCII.GetBytes(stringBuilder.ToString());
		Timeout();
		stream.Write(bytes, 0, bytes.Length);
	}

	public void FileReply(Stream source, AssetRecord record, HttpRequest request)
	{
		long num = 0L;
		long num2 = record.Length - 1;
		int status = 200;
		Dictionary<string, string> dictionary = new Dictionary<string, string>
		{
			{
				"ETag",
				"\"" + record.Sha256 + "\""
			},
			{ "Accept-Ranges", "bytes" }
		};
		if (request.Headers.TryGetValue("Range", out var value))
		{
			if (!value.StartsWith("bytes=", StringComparison.Ordinal) || value.IndexOf(',') >= 0)
			{
				throw new ProtocolError(416, "Only one byte range is supported.");
			}
			string[] array = value.Substring(6).Split('-');
			if (array.Length != 2)
			{
				throw new ProtocolError(416, "Invalid byte range.");
			}
			long result;
			if (array[0].Length == 0)
			{
				if (!long.TryParse(array[1], NumberStyles.None, CultureInfo.InvariantCulture, out result) || result <= 0)
				{
					throw new ProtocolError(416, "Invalid suffix range.");
				}
				num = Math.Max(0L, record.Length - result);
			}
			else
			{
				if (!long.TryParse(array[0], NumberStyles.None, CultureInfo.InvariantCulture, out var result2))
				{
					throw new ProtocolError(416, "Invalid range start.");
				}
				num = result2;
				if (array[1].Length != 0)
				{
					if (!long.TryParse(array[1], NumberStyles.None, CultureInfo.InvariantCulture, out result))
					{
						throw new ProtocolError(416, "Invalid range end.");
					}
					num2 = Math.Min(num2, result);
				}
			}
			if (num >= record.Length || num2 < num)
			{
				throw new ProtocolError(416, "Range is outside the stored asset.");
			}
			status = 206;
			dictionary.Add("Content-Range", "bytes " + num + "-" + num2 + "/" + record.Length);
		}
		Header(status, num2 - num + 1, "application/octet-stream", dictionary);
		if (request.Method != "HEAD")
		{
			source.Position = num;
			long num3 = num2 - num + 1;
			byte[] array2 = new byte[65536];
			while (num3 > 0)
			{
				int num4 = source.Read(array2, 0, (int)Math.Min(array2.Length, num3));
				if (num4 == 0)
				{
					throw new IOException("Stored asset truncated during readback.");
				}
				Timeout();
				stream.Write(array2, 0, num4);
				num3 -= num4;
			}
		}
		stream.Flush();
	}

	private static string Reason(int code)
	{
		return code switch
		{
			200 => "OK", 
			206 => "Partial Content", 
			400 => "Bad Request", 
			403 => "Forbidden", 
			404 => "Not Found", 
			405 => "Method Not Allowed", 
			408 => "Request Timeout", 
			409 => "Conflict", 
			411 => "Length Required", 
			413 => "Payload Too Large", 
			416 => "Range Not Satisfiable", 
			417 => "Expectation Failed", 
			422 => "Unprocessable Content", 
			431 => "Request Header Fields Too Large", 
			501 => "Not Implemented", 
			503 => "Service Unavailable", 
			507 => "Insufficient Storage", 
			_ => "Internal Server Error", 
		};
	}
}
