using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Frosty.Core;
using FrostySdk;
using FrostySdk.Interfaces;
using OfflineTeamBuilder.Repair;

namespace CFB27TeamBuilderOfflineFrostyRuntime;

public sealed class TeamBuilderOfflineExecutionAction : ExecutionAction
{
	private static readonly object Sync = new object();

	private static readonly object BootstrapSync = new object();

	private static Thread worker;

	private static string status = "Installed. v1.1.17 stock saved-asset lifecycle repair starts automatically after College Football 27 launches.";

	private static int lastExitCode = int.MinValue;

	private static string bootstrapLogPath = "";

	private static string outputRoot = "";

	private static bool launchTokenCancelled;

	internal static string Status
	{
		get
		{
			lock (Sync)
			{
				return status;
			}
		}
	}

	internal static int LastExitCode
	{
		get
		{
			lock (Sync)
			{
				return lastExitCode;
			}
		}
	}

	public override Action<ILogger, PluginManagerType, CancellationToken> PreLaunchAction => delegate(ILogger logger, PluginManagerType type, CancellationToken token)
	{
		Log(logger, OfflineTeamBuilderRuntime.PluginIdentity.DisplayName + " " + OfflineTeamBuilderRuntime.PluginIdentity.Version + ": original saves are left unchanged; checking optional copy-job configuration.");
		try
		{
			if (ProfilesLibrary.IsLoaded(ProfileVersion.CollegeFootball27))
			{
				string path = Path.Combine(Path.GetDirectoryName(typeof(TeamBuilderOfflineExecutionAction).Assembly.Location), OfflineTeamBuilderRuntime.RuntimePaths.OptionsFileName);
				if (File.Exists(path))
				{
					RepairOptions repairOptions = RepairJson.Decode<RepairOptions>(File.ReadAllBytes(path));
					if (repairOptions.CareerCopies != null && repairOptions.CareerCopies.Length != 0)
					{
						if (Process.GetProcessesByName("CollegeFB27").Length != 0)
						{
							Log(logger, "CAREER COPY SKIPPED: close the game before preparing copies.");
						}
						else
						{
							string storeDirectory = (string.IsNullOrWhiteSpace(repairOptions.StoreDirectory) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineTeamBuilder", "local-assets-v111") : repairOptions.StoreDirectory);
							CareerCopyJob[] careerCopies = repairOptions.CareerCopies;
							foreach (CareerCopyJob careerCopyJob in careerCopies)
							{
								if (token.IsCancellationRequested)
								{
									break;
								}
								if (careerCopyJob == null || string.IsNullOrWhiteSpace(careerCopyJob.Source) || string.IsNullOrWhiteSpace(careerCopyJob.Destination) || !Path.IsPathRooted(careerCopyJob.Source) || !Path.IsPathRooted(careerCopyJob.Destination))
								{
									Log(logger, "CAREER COPY SKIPPED: both explicit paths must be absolute.");
								}
								else if (File.Exists(careerCopyJob.Destination))
								{
									Log(logger, "CAREER COPY EXISTS, left untouched: " + careerCopyJob.Destination);
								}
								else
								{
									try
									{
										CareerBindingReport careerBindingReport = CareerUrlBinding.CreateLocalCopy(careerCopyJob.Source, careerCopyJob.Destination, storeDirectory);
										Log(logger, "CAREER URL COPY PREPARED: " + careerCopyJob.Destination + " changed URL fields=" + careerBindingReport.Changes.Count + ". Native acceptance of absolute URLs still requires testing.");
									}
									catch (Exception ex)
									{
										Log(logger, "CAREER COPY FAILED (original untouched): " + ex.Message);
									}
								}
							}
						}
					}
				}
			}
		}
		catch (Exception ex2)
		{
			Log(logger, "CAREER COPY CONFIGURATION ERROR: " + ex2.Message);
		}
	};

	public override Action<ILogger, PluginManagerType, CancellationToken> PostLaunchAction => delegate(ILogger logger, PluginManagerType type, CancellationToken token)
	{
		StartRuntime(logger, type, token);
	};

	private static void SetStatus(string value)
	{
		lock (Sync)
		{
			status = value;
		}
	}

	private static void Log(ILogger logger, string text)
	{
		try
		{
			logger.Log("[Offline TeamBuilder] " + text);
		}
		catch
		{
		}
	}

	private static void Bootstrap(string text)
	{
		string text2;
		lock (Sync)
		{
			text2 = bootstrapLogPath;
		}
		if (string.IsNullOrEmpty(text2))
		{
			return;
		}
		lock (BootstrapSync)
		{
			try
			{
				File.AppendAllText(text2, DateTime.Now.ToString("O", CultureInfo.InvariantCulture) + "  " + text + Environment.NewLine);
			}
			catch
			{
			}
		}
	}

	private static bool TryInitializeOutput(ILogger logger, PluginManagerType managerType, CancellationToken token)
	{
		string fileName;
		string directoryName;
		try
		{
			fileName = Process.GetCurrentProcess().MainModule.FileName;
			directoryName = Path.GetDirectoryName(fileName);
			if (string.IsNullOrEmpty(directoryName))
			{
				throw new InvalidOperationException("Host executable directory was empty.");
			}
		}
		catch (Exception ex)
		{
			Log(logger, "ERROR: could not resolve the running Mod Manager executable path: " + ex.Message);
			return false;
		}
		launchTokenCancelled = token.IsCancellationRequested;
		string text = Path.Combine(directoryName, "Output");
		string text2 = Path.Combine(text, "OfflineTeamBuilder");
		string text3 = Path.Combine(text2, "OfflineTeamBuilder_bootstrap.log");
		try
		{
			Directory.CreateDirectory(text2);
			using (new FileStream(Path.Combine(text2, ".write-test-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
			{
			}
			lock (Sync)
			{
				outputRoot = text;
				bootstrapLogPath = text3;
			}
		}
		catch (Exception ex2)
		{
			Log(logger, "Manager Output is not writable; trying a per-user logging directory: " + ex2.Message);
			try
			{
				text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineTeamBuilder", "manager-output");
				text2 = Path.Combine(text, "OfflineTeamBuilder");
				text3 = Path.Combine(text2, "OfflineTeamBuilder_bootstrap.log");
				Directory.CreateDirectory(text2);
				using (new FileStream(Path.Combine(text2, ".write-test-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
				{
				}
				lock (Sync)
				{
					outputRoot = text;
					bootstrapLogPath = text3;
				}
			}
			catch (Exception ex3)
			{
				Log(logger, "ERROR: no writable output directory: " + ex3.Message);
				return false;
			}
		}
		Bootstrap("");
		Bootstrap("===============================================================================");
		Bootstrap("Offline TeamBuilder v1.1.17 stock saved-asset lifecycle bootstrap launch");
		Bootstrap("===============================================================================");
		Bootstrap("Host process PID: " + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
		Bootstrap("Host Mod Manager executable: " + fileName);
		Bootstrap("Authoritative Mod Manager root: " + directoryName);
		Bootstrap("Authoritative Output root: " + text);
		Bootstrap("Plugin assembly: " + typeof(TeamBuilderOfflineExecutionAction).Assembly.Location);
		Bootstrap("AppDomain base: " + AppDomain.CurrentDomain.BaseDirectory);
		Bootstrap("Current directory: " + Environment.CurrentDirectory);
		Bootstrap("Manager type: " + managerType);
		Bootstrap("Launch token initially cancelled: " + token.IsCancellationRequested);
		try
		{
			token.Register(delegate
			{
				launchTokenCancelled = true;
				Bootstrap("Frosty launch token cancellation observed. Persistent runtime is NOT stopped solely by this token.");
				Log(logger, "Launch token cancellation observed; attached game lifetime remains authoritative.");
			});
		}
		catch (Exception ex4)
		{
			Bootstrap("Launch token registration failed: " + ex4.Message);
		}
		Log(logger, "v1.1.17 Output root: " + text);
		return true;
	}

	private static bool IsExited(Process process)
	{
		if (process == null)
		{
			return true;
		}
		try
		{
			return process.HasExited;
		}
		catch
		{
			return true;
		}
	}

	private static Process WaitForNewestViableGame(ILogger logger)
	{
		Bootstrap("STATE WAIT_FOR_GAME");
		SetStatus("Waiting for CollegeFB27.exe...");
		HashSet<int> hashSet = new HashSet<int>();
		Process process;
		DateTime dateTime;
		while (true)
		{
			Process[] processesByName;
			try
			{
				processesByName = Process.GetProcessesByName("CollegeFB27");
			}
			catch (Exception ex)
			{
				Bootstrap("Process enumeration failed: " + ex.Message);
				Thread.Sleep(1000);
				continue;
			}
			process = null;
			dateTime = DateTime.MinValue;
			Process[] array = processesByName;
			foreach (Process process2 in array)
			{
				string text = "<unreadable>";
				DateTime dateTime2 = DateTime.MinValue;
				int num = 0;
				string text2 = "not viable";
				try
				{
					if (process2.HasExited)
					{
						text2 = "already exited";
					}
					else
					{
						text = process2.MainModule.FileName;
						num = process2.MainModule.ModuleMemorySize;
						dateTime2 = process2.StartTime;
						if (string.Equals(Path.GetFileName(text), "CollegeFB27.exe", StringComparison.OrdinalIgnoreCase) && num > 0)
						{
							text2 = "viable";
						}
					}
				}
				catch (Exception ex2)
				{
					text2 = "unreadable: " + ex2.Message;
				}
				if (!hashSet.Contains(process2.Id))
				{
					hashSet.Add(process2.Id);
					Bootstrap("CollegeFB27 candidate PID=" + process2.Id.ToString(CultureInfo.InvariantCulture) + " start=" + ((dateTime2 == DateTime.MinValue) ? "<unreadable>" : dateTime2.ToString("O", CultureInfo.InvariantCulture)) + " moduleSize=0x" + num.ToString("X", CultureInfo.InvariantCulture) + " path=" + text + " status=" + text2);
				}
				if (text2 == "viable" && (process == null || dateTime2 > dateTime || (dateTime2 == dateTime && process2.Id > process.Id)))
				{
					process = process2;
					dateTime = dateTime2;
				}
			}
			if (process != null)
			{
				break;
			}
			Thread.Sleep(750);
		}
		Bootstrap("STATE ATTACH_PID selected PID=" + process.Id.ToString(CultureInfo.InvariantCulture) + " start=" + dateTime.ToString("O", CultureInfo.InvariantCulture));
		SetStatus("CollegeFB27.exe found. Arming v1.1.17 stock saved-asset lifecycle runtime...");
		Log(logger, "Selected CollegeFB27 PID " + process.Id.ToString(CultureInfo.InvariantCulture) + ".");
		return process;
	}

	private static void StartRuntime(ILogger logger, PluginManagerType managerType, CancellationToken token)
	{
		lock (Sync)
		{
			if (worker != null && worker.IsAlive)
			{
				Log(logger, "Runtime already active; duplicate launch action ignored.");
				return;
			}
		}
		if (!TryInitializeOutput(logger, managerType, token))
		{
			return;
		}
		if (!ProfilesLibrary.IsLoaded(ProfileVersion.CollegeFootball27))
		{
			Bootstrap("ABORT: College Football 27 profile is not loaded in this Mod Manager process.");
			Log(logger, "College Football 27 profile is not loaded; runtime was not started.");
			return;
		}
		lock (Sync)
		{
			lastExitCode = int.MinValue;
			status = "v1.1.17 stock saved-asset lifecycle runtime starting.";
			worker = new Thread((ThreadStart)delegate
			{
				try
				{
					Log(logger, "v1.1.17 stock saved-asset lifecycle runtime thread started.");
					Bootstrap("Runtime worker started. launchTokenCancelled=" + launchTokenCancelled);
					CFB27TeamBuilderBatchQueuePreMcrProbe.BootstrapLog = delegate(string line)
					{
						Bootstrap(line);
					};
					CFB27TeamBuilderBatchQueuePreMcrProbe.ExternalLog = delegate(string line)
					{
						Log(logger, line);
						if (line.IndexOf("Waiting for TeamBuilder native singleton", StringComparison.Ordinal) >= 0)
						{
							SetStatus("Waiting for the live TeamBuilder native singleton/vtable...");
						}
						if (line.IndexOf("TRANSPORT READY - LOCAL STORAGE ACTIVE; NATIVE VISUAL INITIALIZATION UNVERIFIED", StringComparison.Ordinal) >= 0)
						{
							SetStatus("TRANSPORT READY. Visual initialization and cold reload still require testing.");
						}
						if (line.IndexOf("NATIVE HOOK HEALTH: DEGRADED", StringComparison.Ordinal) >= 0 || line.IndexOf("LOCAL SERVICE FAILURE:", StringComparison.Ordinal) >= 0 || line.IndexOf("MCR GUARD FAILURE:", StringComparison.Ordinal) >= 0 || line.IndexOf("FAIL ", StringComparison.Ordinal) >= 0 || line.IndexOf("FAIL:", StringComparison.Ordinal) >= 0 || line.IndexOf("ABORT:", StringComparison.Ordinal) >= 0)
						{
							SetStatus(line);
						}
					};
					int num;
					while (true)
					{
						Process process = WaitForNewestViableGame(logger);
						CFB27TeamBuilderBatchQueuePreMcrProbe.LastSessionReachedReady = false;
						num = CFB27TeamBuilderBatchQueuePreMcrProbe.RunEmbeddedSession(process, outputRoot);
						lock (Sync)
						{
							lastExitCode = num;
						}
						bool flag = IsExited(process);
						Bootstrap("RUNTIME_SESSION_ENDED exitCode=" + num.ToString(CultureInfo.InvariantCulture) + " reachedReady=" + CFB27TeamBuilderBatchQueuePreMcrProbe.LastSessionReachedReady + " gameExited=" + flag);
						if (CFB27TeamBuilderBatchQueuePreMcrProbe.LastSessionReachedReady || !flag)
						{
							break;
						}
						Bootstrap("Selected PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " exited before READY. Clearing PID-specific state and returning to WAIT_FOR_GAME.");
						SetStatus("Game process changed before Offline TeamBuilder was ready; waiting for CollegeFB27.exe again...");
						Thread.Sleep(500);
					}
					if (num == 0)
					{
						SetStatus("v1.1.17 runtime session ended normally.");
						Log(logger, Status);
					}
					else
					{
						SetStatus("v1.1.17 runtime ended with code " + num.ToString(CultureInfo.InvariantCulture) + ". See OfflineTeamBuilder diagnostics.");
						Log(logger, Status);
					}
				}
				catch (Exception ex)
				{
					lock (Sync)
					{
						lastExitCode = 99;
					}
					SetStatus("Runtime plugin exception: " + ex.Message);
					Bootstrap("FATAL wrapper exception: " + ex.ToString());
					Log(logger, ex.ToString());
				}
				finally
				{
					CFB27TeamBuilderBatchQueuePreMcrProbe.ExternalLog = null;
					CFB27TeamBuilderBatchQueuePreMcrProbe.BootstrapLog = null;
				}
			});
			worker.IsBackground = true;
			worker.Name = "Offline TeamBuilder v1.1.17 stock saved-asset lifecycle runtime";
			worker.Start();
		}
	}
}
