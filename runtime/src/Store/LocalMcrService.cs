using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace OfflineTeamBuilder.Repair;

public sealed class LocalMcrService : IDisposable
{
	public readonly object Sync = new object();

	private readonly object logSync = new object();

	private readonly object clientsSync = new object();

	private readonly Dictionary<string, MetadataRecord> pending = new Dictionary<string, MetadataRecord>(StringComparer.Ordinal);

	private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();

	private readonly ManualResetEvent drained = new ManualResetEvent(initialState: true);

	private readonly RepairOptions options;

	private readonly LocalAssetStore store;

	private readonly string logFile;

	private int number;

	private int errors;

	private bool nativeHealthy;

	private string nativeState = "NOT_ARMED";

	private bool disposed;

	private string lastError = "";

	public volatile bool StopRequested;

	public TcpListener Listener { get; private set; }

	public Thread Worker { get; private set; }

	public int Port { get; private set; }

	public string RootDirectory { get; private set; }

	public string ErrorText { get; private set; } = "";

	public int CompletedImports => store.CompleteGroups;

	public int RequestCount => Volatile.Read(ref number);

	public string GuardFailure
	{
		get
		{
			lock (Sync)
			{
				return lastError;
			}
		}
	}

	public bool IsHealthy
	{
		get
		{
			if (!StopRequested && Worker != null && Worker.IsAlive)
			{
				return ErrorText.Length == 0;
			}
			return false;
		}
	}

	public string StoreDirectory => store.Root;

	public LocalMcrService(string sessionDirectory, RepairOptions options)
	{
		this.options = options;
		options.Validate();
		RootDirectory = Path.Combine(sessionDirectory, "loopback-mcr");
		Directory.CreateDirectory(RootDirectory);
		logFile = Path.Combine(RootDirectory, "requests.jsonl");
		store = new LocalAssetStore(options);
		foreach (string recoveryWarning in store.RecoveryWarnings)
		{
			Log(0, "RECOVERY_WARNING", "", 0, recoveryWarning);
		}
	}

	public void Start()
	{
		Exception innerException = null;
		for (int i = 27875; i <= 27875; i++)
		{
			TcpListener tcpListener = null;
			try
			{
				tcpListener = new TcpListener(IPAddress.Loopback, i);
				tcpListener.Start(8);
				Listener = tcpListener;
				Port = i;
			}
			catch (Exception ex) when (ex is SocketException || ex is IOException)
			{
				innerException = ex;
				tcpListener?.Stop();
				continue;
			}
			break;
		}
		if (Listener == null)
		{
			throw new IOException("Cannot bind fixed local-readback port 27875. Close conflicting instances; do not silently use another port with already-bound careers.", innerException);
		}
		Worker = new Thread(AcceptLoop)
		{
			IsBackground = true,
			Name = "Offline TeamBuilder local asset service"
		};
		Worker.Start();
		Log(0, "SERVICE_STARTED", "", 200, "Durable store=" + store.Root + "; native readback contract still requires in-game verification.");
	}

	private void AcceptLoop()
	{
		try
		{
			while (!StopRequested)
			{
				TcpClient client;
				try
				{
					client = Listener.AcceptTcpClient();
				}
				catch (SocketException)
				{
					if (StopRequested)
					{
						break;
					}
					throw;
				}
				catch (ObjectDisposedException)
				{
					if (StopRequested)
					{
						break;
					}
					throw;
				}
				bool flag;
				lock (clientsSync)
				{
					flag = clients.Count < options.MaximumClients;
					if (flag)
					{
						clients.Add(client);
						drained.Reset();
					}
				}
				if (!flag)
				{
					try
					{
						using (client)
						{
							new HttpProtocol(client.GetStream(), options).Reply(503, RepairJson.Encode("Local service is busy; retry this same operation."));
						}
					}
					catch
					{
						client.Close();
					}
				}
				else
				{
					ThreadPool.QueueUserWorkItem(delegate
					{
						Process(client);
					});
				}
			}
		}
		catch (Exception ex3)
		{
			ErrorText = ex3.GetType().Name + ": " + ex3.Message;
			Log(0, "ACCEPT_LOOP_FAILED", "", 503, ErrorText);
		}
	}

	private void Process(TcpClient client)
	{
		int request = Interlocked.Increment(ref number);
		string text = null;
		HttpProtocol httpProtocol = null;
		HttpRequest httpRequest = null;
		string text2 = "";
		try
		{
			NetworkStream stream = client.GetStream();
			client.NoDelay = true;
			httpProtocol = new HttpProtocol(stream, options);
			httpRequest = httpProtocol.ReadHeaders(Port);
			text2 = httpRequest.Path;
			Log(request, "REQUEST_RECEIVED", httpRequest.TargetForDiagnostics, 0, "method=" + httpRequest.Method);
			if (httpRequest.Method == "GET" && (text2 == "/offline-tb/health" || text2 == "/health"))
			{
				httpProtocol.Reply(IsHealthy ? 200 : 503, RepairJson.Encode(Snapshot()));
				return;
			}
			if (httpRequest.Method == "POST" && (text2 == "/api/" || text2 == "/api"))
			{
				if (httpRequest.Length.GetValueOrDefault() > options.MaximumMetadataBytes)
				{
					throw new ProtocolError(413, "Metadata exceeds endpoint limit.");
				}
				httpProtocol.Continue(httpRequest);
				byte[] array;
				using (MemoryStream memoryStream = new MemoryStream())
				{
					httpProtocol.ReadBody(httpRequest, memoryStream, options.MaximumMetadataBytes);
					array = memoryStream.ToArray();
				}
				MetadataRecord metadataRecord = RepairJson.ParseMetadata(array, options.MaximumMetadataBytes);
				lock (Sync)
				{
					string[] array2 = (from p in pending
						where DateTime.UtcNow - p.Value.SeenUtc > TimeSpan.FromMinutes(10.0)
						select p.Key).ToArray();
					foreach (string key in array2)
					{
						pending.Remove(key);
					}
					if (!pending.ContainsKey(metadataRecord.Id) && pending.Count >= 512)
					{
						throw new ProtocolError(503, "Too many pending artifact descriptors; retry later.");
					}
					pending[metadataRecord.Id] = metadataRecord;
					lastError = "";
				}
				try
				{
					RepairJson.AtomicWrite(Path.Combine(RootDirectory, "metadata_" + request.ToString("D6") + ".json"), array);
				}
				catch (IOException ex)
				{
					Log(request, "DIAGNOSTIC_WRITE_WARNING", text2, 0, ex.Message);
				}
				catch (UnauthorizedAccessException ex2)
				{
					Log(request, "DIAGNOSTIC_WRITE_WARNING", text2, 0, ex2.Message);
				}
				httpProtocol.Reply(200, new byte[0]);
				Log(request, "METADATA_ACCEPTED_OR_REPLAYED", text2, 200, metadataRecord.Id);
				return;
			}
			string text3 = UploadId(text2);
			if (httpRequest.Method == "PUT" && text3 != null)
			{
				MetadataRecord value;
				lock (Sync)
				{
					pending.TryGetValue(text3, out value);
				}
				if (value == null)
				{
					AssetRecord assetRecord = store.Get(text3);
					if (assetRecord != null)
					{
						value = new MetadataRecord
						{
							Id = assetRecord.Id,
							Kind = assetRecord.Kind,
							Group = assetRecord.Group
						};
					}
				}
				if (value == null)
				{
					throw new ProtocolError(409, "Metadata for this artifact is required before upload. Other teams' progress is unaffected.");
				}
				if (!httpRequest.Chunked && httpRequest.Length.GetValueOrDefault() == 0L)
				{
					throw new ProtocolError(422, "A required visual artifact cannot be empty.");
				}
				httpProtocol.Continue(httpRequest);
				text = store.NewStagingFile();
				long num2;
				using (FileStream fileStream = new FileStream(text, FileMode.CreateNew, FileAccess.Write, FileShare.None))
				{
					num2 = httpProtocol.ReadBody(httpRequest, fileStream, options.MaximumUploadBytes);
					fileStream.Flush(flushToDisk: true);
				}
				if (num2 == 0L)
				{
					throw new ProtocolError(422, "A required visual artifact cannot be empty.");
				}
				string text4;
				try
				{
					text4 = AssetValidation.Validate(text, value.Kind, options);
				}
				catch (InvalidDataException ex3)
				{
					throw new ProtocolError(422, ex3.Message);
				}
				if (StopRequested)
				{
					throw new ProtocolError(503, "Service is stopping; this upload was not committed.");
				}
				bool replay;
				bool revised;
				string text5 = store.Commit(value, text, text2, text4, out replay, out revised);
				text = null;
				lock (Sync)
				{
					lastError = "";
				}
				httpProtocol.Reply(200, new byte[0]);
				Log(request, revised ? "UPLOAD_VALIDATED_REVISION_PROMOTED" : (replay ? "UPLOAD_REPLAY_VERIFIED" : "UPLOAD_VALIDATED_AND_STORED"), text2, 200, value.Id + " sha256=" + text5 + " bytes=" + num2 + " validation=" + text4);
				return;
			}
			if ((httpRequest.Method == "GET" || httpRequest.Method == "HEAD") && text2.StartsWith("/0/", StringComparison.Ordinal))
			{
				string id;
				try
				{
					id = CareerUrlBinding.LocatorId(text2.Substring(1));
				}
				catch (InvalidDataException ex4)
				{
					throw new ProtocolError(400, ex4.Message);
				}
				AssetRecord assetRecord2 = store.Get(id);
				if (assetRecord2 == null || !assetRecord2.UploadPaths.Contains("/api/upload/-1" + text2))
				{
					throw new ProtocolError(404, "No committed native artifact is bound to this exact saved locator.");
				}
				using (FileStream source = store.OpenVerified(assetRecord2))
				{
					httpProtocol.FileReply(source, assetRecord2, httpRequest);
				}
				Log(request, "SAVED_LOCATOR_READBACK", text2, 200, assetRecord2.Id);
				return;
			}
			if (httpRequest.Method == "GET" || httpRequest.Method == "HEAD")
			{
				bool flag = text2.StartsWith("/offline-tb/assets/", StringComparison.Ordinal);
				if (flag)
				{
					text3 = DecodeComponent(text2.Substring("/offline-tb/assets/".Length));
				}
				if (text3 != null && (flag || options.EnableUploadPathReadback))
				{
					AssetRecord assetRecord3 = store.Get(text3);
					if (assetRecord3 == null || (!flag && !assetRecord3.UploadPaths.Contains(text2)))
					{
						throw new ProtocolError(404, "No committed object is bound to this exact path.");
					}
					using (FileStream source2 = store.OpenVerified(assetRecord3))
					{
						httpProtocol.FileReply(source2, assetRecord3, httpRequest);
					}
					Log(request, flag ? "DIAGNOSTIC_READBACK" : "UPLOAD_PATH_READBACK", text2, 200, assetRecord3.Id);
					return;
				}
			}
			throw new ProtocolError(404, "No verified handler for this route. Unknown native lookup routes are logged, never fabricated as successful.");
		}
		catch (Exception ex5)
		{
			int status = ((ex5 is ProtocolError) ? ((ProtocolError)ex5).Status : ((ex5 is IOException || ex5 is SocketException) ? 408 : 500));
			Interlocked.Increment(ref errors);
			string text6 = ex5.GetType().Name + ": " + ex5.Message;
			lock (Sync)
			{
				lastError = text6;
			}
			if (text != null)
			{
				try
				{
					store.Quarantine(text, text6);
				}
				catch
				{
				}
			}
			try
			{
				httpProtocol?.Reply(status, RepairJson.Encode(text6), "application/json", httpRequest != null && httpRequest.Method == "HEAD");
			}
			catch
			{
			}
			Log(request, "REQUEST_FAILED", text2, status, text6);
		}
		finally
		{
			try
			{
				client.Close();
			}
			catch
			{
			}
			lock (clientsSync)
			{
				clients.Remove(client);
				if (clients.Count == 0)
				{
					drained.Set();
				}
			}
		}
	}

	internal static string UploadId(string path)
	{
		if (!path.StartsWith("/api/upload/", StringComparison.Ordinal))
		{
			return null;
		}
		string[] array = path.Split('/');
		if (array.Length != 7 || array[3].Length == 0 || array[4].Length == 0)
		{
			throw new ProtocolError(400, "Unexpected upload path shape.");
		}
		DecodeComponent(array[3]);
		DecodeComponent(array[4]);
		string text = DecodeComponent(array[5]);
		string text2 = DecodeComponent(array[6]);
		if (text != text2)
		{
			throw new ProtocolError(400, "This supported upload contract requires matching artifact and filename identifiers.");
		}
		RepairJson.Identify(text, out var _, out var _);
		return text;
	}

	private static string DecodeComponent(string component)
	{
		string text;
		try
		{
			text = Uri.UnescapeDataString(component);
		}
		catch
		{
			throw new ProtocolError(400, "Invalid URL encoding.");
		}
		if (text.Length == 0 || text.Length > 512 || text.Any((char c) => char.IsControl(c) || c == '/' || c == '\\'))
		{
			throw new ProtocolError(400, "Invalid path component.");
		}
		return text;
	}

	private void Log(int request, string action, string path, int status, string detail)
	{
		try
		{
			if (!string.IsNullOrEmpty(path))
			{
				path = ReloadDiagnostics.SafeUrl(path);
			}
			Dictionary<string, string> value = new Dictionary<string, string>
			{
				{
					"utc",
					DateTime.UtcNow.ToString("O")
				},
				{
					"request",
					request.ToString(CultureInfo.InvariantCulture)
				},
				{ "action", action },
				{
					"path",
					(path.Length > 1600) ? path.Substring(0, 1600) : path
				},
				{
					"http_status",
					status.ToString(CultureInfo.InvariantCulture)
				},
				{ "detail", detail }
			};
			string text = Encoding.UTF8.GetString(RepairJson.Encode(value));
			lock (logSync)
			{
				File.AppendAllText(logFile, text + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch
		{
		}
	}

	private HealthRecord Snapshot()
	{
		lock (Sync)
		{
			return new HealthRecord
			{
				ServiceAlive = IsHealthy,
				NativeHooksHealthy = nativeHealthy,
				State = ((!IsHealthy) ? "SERVICE_UNAVAILABLE" : (nativeHealthy ? "TRANSPORT_READY_NATIVE_VISUALS_UNVERIFIED" : nativeState)),
				StoreDirectory = store.Root,
				StoredCompleteArtifactGroups = store.CompleteGroups,
				Requests = RequestCount,
				Errors = Volatile.Read(ref errors),
				LastError = lastError,
				UpdatedUtc = DateTime.UtcNow.ToString("O")
			};
		}
	}

	public void SetNativeHealth(bool healthy, string state)
	{
		lock (Sync)
		{
			nativeHealthy = healthy;
			nativeState = state;
		}
		try
		{
			RepairJson.AtomicWrite(Path.Combine(RootDirectory, "runtime-health.json"), RepairJson.Encode(Snapshot()));
		}
		catch
		{
		}
	}

	public void WriteSummary(string directory)
	{
		RepairJson.AtomicWrite(Path.Combine(directory, "local-mcr-summary.json"), RepairJson.Encode(Snapshot()));
	}

	public void Stop()
	{
		StopRequested = true;
		if (Listener != null)
		{
			try
			{
				Listener.Stop();
			}
			catch
			{
			}
		}
		lock (clientsSync)
		{
			TcpClient[] array = clients.ToArray();
			foreach (TcpClient tcpClient in array)
			{
				try
				{
					tcpClient.Close();
				}
				catch
				{
				}
			}
		}
		if (Worker != null && Worker.IsAlive)
		{
			Worker.Join(3000);
		}
		if (!drained.WaitOne(15000))
		{
			Log(0, "SHUTDOWN_WAIT_INCOMPLETE", "", 503, "An in-flight worker still owns the store; restart Mod Manager before another session.");
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			Stop();
			if (drained.WaitOne(0))
			{
				store.Dispose();
				drained.Dispose();
				disposed = true;
			}
		}
	}
}
