using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OfflineTeamBuilderReentry;

internal sealed partial class TeamBuilderReentryRepair : IDisposable
{
	private sealed class CodeBuilder
	{
		private readonly List<byte> _b = new List<byte>();

		public int Position => _b.Count;

		public void B(params byte[] bytes)
		{
			_b.AddRange(bytes);
		}

		public void Bytes(byte[] bytes)
		{
			_b.AddRange(bytes);
		}

		public void U64(ulong value)
		{
			_b.AddRange(BitConverter.GetBytes(value));
		}

		public int Jcc32(byte conditionLowByte)
		{
			int position = Position;
			B(15, conditionLowByte, 0, 0, 0, 0);
			return position;
		}

		public int Jmp32()
		{
			int position = Position;
			B(233, 0, 0, 0, 0);
			return position;
		}

		public void PatchJcc32(int at, int target)
		{
			int num = at + 6;
			int value = checked(target - num);
			byte[] bytes = BitConverter.GetBytes(value);
			for (int i = 0; i < 4; i++)
			{
				_b[at + 2 + i] = bytes[i];
			}
		}

		public void PatchJmp32(int at, int target)
		{
			int num = at + 5;
			int value = checked(target - num);
			byte[] bytes = BitConverter.GetBytes(value);
			for (int i = 0; i < 4; i++)
			{
				_b[at + 1 + i] = bytes[i];
			}
		}

		public byte[] ToArray()
		{
			return _b.ToArray();
		}
	}

	private static class Native
	{
		public const uint PROCESS_VM_READ = 16u;

		public const uint PROCESS_VM_WRITE = 32u;

		public const uint PROCESS_VM_OPERATION = 8u;

		public const uint PROCESS_QUERY_INFORMATION = 1024u;

		public const uint MEM_COMMIT = 4096u;

		public const uint MEM_RESERVE = 8192u;

		public const uint PAGE_EXECUTE_READWRITE = 64u;

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, UIntPtr size, out UIntPtr bytesWritten);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

		[DllImport("kernel32.dll", SetLastError = true)]
		public static extern bool CloseHandle(IntPtr handle);
	}

	private const ulong SlotAdd = 1032uL;

	private const ulong SlotEnter = 1040uL;

	private const ulong SlotNeeds = 1048uL;

	private const ulong SlotReset = 1064uL;

	private const ulong SlotFetch = 1072uL;

	private const ulong SlotCareer = 1240uL;

	private const ulong StCallbacks = 0uL;

	private const ulong StRepairStarted = 4uL;

	private const ulong StStage = 8uL;

	private const ulong StDispatcherCalls = 12uL;

	private const ulong StManager = 16uL;

	private const ulong StRepairB = 24uL;

	private const ulong StSlot = 32uL;

	private const ulong StTeamId = 36uL;

	private const ulong StMapAfterAdd = 40uL;

	private const ulong StNeedsReturn = 44uL;

	private const ulong StFetchCount = 48uL;

	private const ulong StResetCount = 52uL;

	private const ulong StError = 56uL;

	private const ulong StResetReady = 60uL;

	private const ulong StStateAtFetch = 64uL;

	private const ulong StConverterAtFetch = 72uL;

	private const ulong StStateAtReset = 80uL;

	private const ulong StMapAtReset = 88uL;

	private const ulong StCompletedB = 96uL;

	private const ulong StGenerationCount = 104uL;

	private const ulong StLastSeenB = 112uL;

	private static readonly byte[] ExpectedDispatcherPrologue = new byte[16]
	{
		72, 137, 92, 36, 8, 72, 137, 116, 36, 16,
		72, 137, 124, 36, 24, 85
	};

	private readonly Process _game;

	private readonly Action<string> _log;

	private IntPtr _processHandle = IntPtr.Zero;

	private ulong _base;

	private ulong _size;

	private ulong _service;

	private ulong _vtable;

	private ulong _careerSlotAddress;

	private ulong _originalCareer;

	private byte[] _originalDispatcher;

	private ulong _remoteBase;

	private ulong _careerRelay;

	private ulong _dispatcherRelay;

	private ulong _status;

	private bool _careerPatched;

	private bool _dispatcherPatched;

	private bool _installed;

	private bool _disposed;

	private Thread _watchThread;

	private volatile bool _stopRequested;

	private bool _loggedSingletonWait;

	private bool _loggedCareerOwnerWait;

	private bool _loggedCareerRelayWait;

	private ulong _lastLoggedCompletedB;

	// These fields are exercised by the inherited private installer-contract
	// harness via reflection. Runtime ownership lives in profileHooks.
#pragma warning disable CS0169
	private bool _news_callback_owned;

	private byte[] _news_callback_patch;

	private bool _news_root_owned;

	private byte[] _news_root_patch;

	private bool _news_captureComplete_owned;

	private byte[] _news_captureComplete_patch;

	private bool _news_audioDecodedTeam_owned;

	private byte[] _news_audioDecodedTeam_patch;
#pragma warning restore CS0169

	public bool IsInstalled => _installed;

	public TeamBuilderReentryRepair(Process game, Action<string> log)
	{
		if (game == null)
		{
			throw new ArgumentNullException("game");
		}
		_game = game;
		_log = log ?? ((Action<string>)delegate
		{
		});
	}

	public static bool IsExpectedBuild(Process game)
	{
		try
		{
			game.Refresh();
			return game.MainModule != null && OfflineTeamBuilderRuntime.GameProfile.Current.FitsImage(game.MainModule.ModuleMemorySize);
		}
		catch
		{
			return false;
		}
	}

	public bool TryInstall()
	{
		if (_disposed)
		{
			throw new ObjectDisposedException(GetType().FullName);
		}
		if (_installed)
		{
			return true;
		}
		try
		{
			_game.Refresh();
			if (_game.HasExited)
			{
				Log("PLAYNOW_ASSET_PROBE SKIP game already exited");
				return false;
			}
			if (_game.MainModule == null)
			{
				Log("PLAYNOW_ASSET_PROBE SKIP MainModule unavailable");
				return false;
			}
			_base = (ulong)_game.MainModule.BaseAddress.ToInt64();
			_size = (ulong)_game.MainModule.ModuleMemorySize;
			if (!OfflineTeamBuilderRuntime.GameProfile.Current.FitsImage((long)_size))
			{
				Log("ASSET_RUNTIME FAIL_CLOSED IMAGE_BOUNDS moduleSize=0x" + _size.ToString("X") + "; profile targets must fit loaded image (not an exact-size requirement).");
				return false;
			}
			uint access = 1080u;
			_processHandle = Native.OpenProcess(access, inheritHandle: false, (uint)_game.Id);
			if (_processHandle == IntPtr.Zero)
			{
				Log("PLAYNOW_ASSET_PROBE FAIL_CLOSED OpenProcess error=" + Marshal.GetLastWin32Error());
				return false;
			}
			_service = ReadU64(_base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceGlobal")));
			if (!SanePointer(_service))
			{
				if (!_loggedSingletonWait)
				{
					_loggedSingletonWait = true;
					Log("PLAYNOW_ASSET_PROBE WAIT TeamBuilder singleton null/not ready (retries suppressed)");
				}
				CloseHandleOnly();
				return false;
			}
			_vtable = ReadU64(_service);
			ulong num = _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceVtable"));
			if (_vtable != num)
			{
				Log("PLAYNOW_ASSET_PROBE FAIL_CLOSED vtable=" + H(_vtable) + " expected=" + H(num));
				CloseHandleOnly();
				return false;
			}
			if (!ValidateLifecycleTarget("Add", 1032uL, ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Add"))) || !ValidateLifecycleTarget("Enter", 1040uL, ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Enter"))) || !ValidateLifecycleTarget("Needs", 1048uL, ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Needs"))) || !ValidateLifecycleTarget("Reset", 1064uL, ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Reset"))) || !ValidateLifecycleTarget("Fetch", 1072uL, ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Fetch"))))
			{
				CloseHandleOnly();
				return false;
			}
			_careerSlotAddress = _vtable + 1240;
			_originalCareer = ReadU64(_careerSlotAddress);
			if (!SanePointer(_originalCareer))
			{
				if (!_loggedCareerOwnerWait)
				{
					_loggedCareerOwnerWait = true;
					Log("PLAYNOW_ASSET_PROBE WAIT current +0x4D8 owner is not ready (retries suppressed)");
				}
				CloseHandleOnly();
				return false;
			}
			if (InGameModule(_originalCareer))
			{
				if (!_loggedCareerRelayWait)
				{
					_loggedCareerRelayWait = true;
					Log("PLAYNOW_ASSET_PROBE WAIT existing OfflineTeamBuilder CareerHub relay not armed yet; currentOwner=" + H(_originalCareer) + " (retries suppressed)");
				}
				CloseHandleOnly();
				return false;
			}
			Log("PLAYNOW_ASSET_PROBE chainCareerOwner=" + H(_originalCareer));
			ulong num2 = _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Dispatcher"));
			_originalDispatcher = Read(num2, 16);
			if (!BytesEqual(_originalDispatcher, ExpectedDispatcherPrologue))
			{
				Log("PLAYNOW_ASSET_PROBE FAIL_CLOSED UCM dispatcher prologue mismatch");
				CloseHandleOnly();
				return false;
			}
			IntPtr intPtr = Native.VirtualAllocEx(_processHandle, IntPtr.Zero, new UIntPtr(65536uL), 12288u, 64u);
			if (intPtr == IntPtr.Zero)
			{
				Log("PLAYNOW_ASSET_PROBE FAIL_CLOSED VirtualAllocEx error=" + Marshal.GetLastWin32Error());
				CloseHandleOnly();
				return false;
			}
			_remoteBase = (ulong)intPtr.ToInt64();
			_careerRelay = _remoteBase;
			_dispatcherRelay = _remoteBase + 8192;
			_status = _remoteBase + 16384;
			byte[] bytes = BuildCareerRelay(_status, _originalCareer, _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("CareerManagerVtable")), _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Add")), _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Needs")), _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Enter")));
			byte[] bytes2 = BuildDispatcherRelay(_status, num2 + 16);
			Write(_careerRelay, bytes);
			Write(_dispatcherRelay, bytes2);
			SetCodePatch(num2, BuildAbsEntryPatch(_dispatcherRelay));
			_dispatcherPatched = true;
			SetPointer(_careerSlotAddress, _careerRelay);
			_careerPatched = true;
			InstallNewsPreview();
			_installed = true;
			_stopRequested = false;
			_watchThread = new Thread(WatchLoop);
			_watchThread.IsBackground = true;
			_watchThread.Name = "Offline TeamBuilder Runtime asset monitor";
			_watchThread.Start();
			Log("PLAYNOW_ASSET_PROBE ARMED base=" + H(_base) + " service=" + H(_service) + " careerRelay=" + H(_careerRelay) + " dispatcherRelay=" + H(_dispatcherRelay));
			Log("ASSET_RUNTIME_READY multi-team visuals, logos, gated news previews and stadium audio; normal hub loading.");
			return true;
		}
		catch (Exception ex)
		{
			Log("PLAYNOW_ASSET_PROBE ERROR " + ex.GetType().Name + ": " + ex.Message);
			SafeUninstall();
			return false;
		}
	}

	private void WatchLoop()
	{
		string previous = null;
        while (!_stopRequested && GameAlive()) {
            try {
                string summary = CaptureSummary();
                if (summary != previous) { Log("ASSET_STATUS " + summary); previous = summary; }
            } catch (Exception ex) { Log("ASSET_MONITOR_ERROR " + ex.Message); }
            Thread.Sleep(1000);
        }
	}

	private void LogOncePerCompletedB(ulong b)
	{
		if (_lastLoggedCompletedB != b)
		{
			_lastLoggedCompletedB = b;
			Log("PLAYNOW_ASSET_PROBE COMPLETE B=" + H(b) + " underlying TeamBuilder state reconstructed. Enter Practice once and quit back to Dynasty to activate assets.");
		}
	}

	private static byte[] BuildCareerRelay(ulong status, ulong chainTarget, ulong expectedBTable, ulong addTarget, ulong needsTarget, ulong enterTarget)
	{
		return OfflineTeamBuilderRuntime.GameProfile.Current.Emit("Career", unchecked((ulong)status), unchecked((ulong)chainTarget), unchecked((ulong)expectedBTable), unchecked((ulong)addTarget), unchecked((ulong)needsTarget), unchecked((ulong)enterTarget));
	}

	private static int FailureLabel(CodeBuilder c, ulong ignoredOffset, byte code)
	{
		int position = c.Position;
		c.B(65, 199, 66, 56, code, 0, 0, 0);
		return position;
	}

	private static byte[] BuildDispatcherRelay(ulong status, ulong resumeAddress)
	{
		return OfflineTeamBuilderRuntime.GameProfile.Current.Emit("Dispatcher", unchecked((ulong)status), unchecked((ulong)resumeAddress));
	}

	private static byte[] BuildAbsEntryPatch(ulong relayAddress)
	{
		CodeBuilder codeBuilder = new CodeBuilder();
		codeBuilder.B(73, 187);
		codeBuilder.U64(relayAddress);
		codeBuilder.B(65, 255, 227);
		while (codeBuilder.Position < 16)
		{
			codeBuilder.B(144);
		}
		return codeBuilder.ToArray();
	}

	private bool ValidateLifecycleTarget(string name, ulong slot, ulong expectedRva)
	{
		ulong num = ReadU64(_vtable + slot);
		ulong num2 = _base + expectedRva;
		if (num != num2)
		{
			Log("PLAYNOW_ASSET_PROBE FAIL_CLOSED " + name + " slot +0x" + slot.ToString("X") + "=" + H(num) + " expected=" + H(num2));
			return false;
		}
		return true;
	}

	private string Snapshot(ulong manager)
	{
		if (!SanePointer(manager))
		{
			return "manager=null";
		}
		ulong num = ReadU64(manager + 1128);
		ulong v = (SanePointer(num) ? ReadU64(num + 432) : 0);
		ulong v2 = ReadU64(manager + 1968);
		byte b = ReadU8(manager + 1971);
		uint num2 = ReadU32(manager + 2008);
		return "manager=" + H(manager) + " B=" + H(num) + " converter=" + H(v) + " state7B0=" + H(v2) + " flag7B3=" + b + " map=" + num2;
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			SafeUninstall();
		}
	}

	public void SafeUninstall()
	{
		_stopRequested = true;
		Thread watchThread = _watchThread;
		if (watchThread != null && watchThread.IsAlive && Thread.CurrentThread != watchThread)
		{
			try
			{
				watchThread.Join(1500);
			}
			catch
			{
			}
		}
		if (_processHandle != IntPtr.Zero)
		{
			RestoreNewsPreview();
			if (_careerPatched && SanePointer(_vtable))
			{
				try
				{
					ulong num = ReadU64(_careerSlotAddress);
					if (num == _careerRelay)
					{
						SetPointer(_careerSlotAddress, _originalCareer);
						Log("PLAYNOW_ASSET_PROBE CLEANUP CareerHub +0x4D8 restored");
					}
					else
					{
						Log("PLAYNOW_ASSET_PROBE CLEANUP_SKIP CareerHub owner changed; not overwriting");
					}
				}
				catch (Exception ex)
				{
					Log("PLAYNOW_ASSET_PROBE CLEANUP_ERROR CareerHub " + ex.Message);
				}
			}
			if (_dispatcherPatched && _originalDispatcher != null)
			{
				try
				{
					ulong address = _base + ((ulong)OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Dispatcher"));
					byte[] a = Read(address, 16);
					byte[] b = BuildAbsEntryPatch(_dispatcherRelay);
					if (BytesEqual(a, b))
					{
						SetCodePatch(address, _originalDispatcher);
						Log("PLAYNOW_ASSET_PROBE CLEANUP UCM dispatcher restored");
					}
					else
					{
						Log("PLAYNOW_ASSET_PROBE CLEANUP_SKIP dispatcher changed; not overwriting");
					}
				}
				catch (Exception ex2)
				{
					Log("PLAYNOW_ASSET_PROBE CLEANUP_ERROR dispatcher " + ex2.Message);
				}
			}
			CloseHandleOnly();
		}
		_installed = false;
		_careerPatched = false;
		_dispatcherPatched = false;
	}

	private bool GameAlive()
	{
		try
		{
			_game.Refresh();
			return !_game.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private void CloseHandleOnly()
	{
		if (_processHandle != IntPtr.Zero)
		{
			Native.CloseHandle(_processHandle);
			_processHandle = IntPtr.Zero;
		}
	}

	private byte[] Read(ulong address, int count)
	{
		if (_processHandle == IntPtr.Zero)
		{
			return null;
		}
		if (!SanePointer(address) || count <= 0)
		{
			return null;
		}
		byte[] array = new byte[count];
		if (!Native.ReadProcessMemory(_processHandle, new IntPtr((long)address), array, new UIntPtr((ulong)count), out var bytesRead) || bytesRead.ToUInt64() != (ulong)count)
		{
			return null;
		}
		return array;
	}

	private ulong ReadU64(ulong address)
	{
		byte[] array = Read(address, 8);
		if (array != null)
		{
			return BitConverter.ToUInt64(array, 0);
		}
		return 0uL;
	}

	private uint ReadU32(ulong address)
	{
		byte[] array = Read(address, 4);
		if (array != null)
		{
			return BitConverter.ToUInt32(array, 0);
		}
		return 0u;
	}

	private ushort ReadU16(ulong address)
	{
		byte[] array = Read(address, 2);
		if (array != null)
		{
			return BitConverter.ToUInt16(array, 0);
		}
		return 0;
	}

	private byte ReadU8(ulong address)
	{
		byte[] array = Read(address, 1);
		if (array != null)
		{
			return array[0];
		}
		return 0;
	}

	private void WriteU32(ulong address, uint value)
	{
		Write(address, BitConverter.GetBytes(value));
	}

	private void Write(ulong address, byte[] bytes)
	{
		if (!Native.WriteProcessMemory(_processHandle, new IntPtr((long)address), bytes, new UIntPtr((ulong)bytes.Length), out var bytesWritten) || bytesWritten.ToUInt64() != (ulong)bytes.Length)
		{
			throw new InvalidOperationException("WriteProcessMemory failed at " + H(address));
		}
	}

	private void SetPointer(ulong address, ulong value)
	{
		if (!Native.VirtualProtectEx(_processHandle, new IntPtr((long)address), new UIntPtr(8uL), 64u, out var oldProtect))
		{
			throw new InvalidOperationException("VirtualProtectEx failed at " + H(address));
		}
		try
		{
			Write(address, BitConverter.GetBytes(value));
		}
		finally
		{
			Native.VirtualProtectEx(_processHandle, new IntPtr((long)address), new UIntPtr(8uL), oldProtect, out var _);
		}
	}

	private void SetCodePatch(ulong address, byte[] bytes)
	{
		if (!Native.VirtualProtectEx(_processHandle, new IntPtr((long)address), new UIntPtr((ulong)bytes.Length), 64u, out var oldProtect))
		{
			throw new InvalidOperationException("VirtualProtectEx failed at " + H(address));
		}
		try
		{
			Write(address, bytes);
			Native.FlushInstructionCache(_processHandle, new IntPtr((long)address), new UIntPtr((ulong)bytes.Length));
		}
		finally
		{
			Native.VirtualProtectEx(_processHandle, new IntPtr((long)address), new UIntPtr((ulong)bytes.Length), oldProtect, out var _);
		}
	}

	private void Log(string s)
	{
		try
		{
			_log(s);
		}
		catch
		{
		}
	}

	private static bool SanePointer(ulong p)
	{
		if (p >= 65536)
		{
			return p < 140737488355328L;
		}
		return false;
	}

	private static bool BytesEqual(byte[] a, byte[] b)
	{
		if (a == null || b == null || a.Length != b.Length)
		{
			return false;
		}
		for (int i = 0; i < a.Length; i++)
		{
			if (a[i] != b[i])
			{
				return false;
			}
		}
		return true;
	}

	private bool InGameModule(ulong address)
	{
		if (address >= _base)
		{
			return address < _base + _size;
		}
		return false;
	}

	private static string H(ulong v)
	{
		return "0x" + v.ToString("X16");
	}

	private static byte[] BuildNewsPreviewRelay(ulong P_0, ulong P_1, ulong P_2)
	{
		return OfflineTeamBuilderRuntime.GameProfile.Current.Emit("News", P_0, P_1, P_2);
	}

	private static byte[] BuildNewsPreviewEntry(ulong P_0, int P_1)
	{
		byte[] array = new byte[P_1];
		if (P_1 > 0)
		{
			array[0] = byte.MaxValue;
		}
		if (P_1 > 1)
		{
			array[1] = 37;
		}
		if (P_1 > 2)
		{
			array[2] = 0;
		}
		if (P_1 > 3)
		{
			array[3] = 0;
		}
		if (P_1 > 4)
		{
			array[4] = 0;
		}
		if (P_1 > 5)
		{
			array[5] = 0;
		}
		if (P_1 > 6)
		{
			array[6] = (byte)(P_0 >> 0);
		}
		if (P_1 > 7)
		{
			array[7] = (byte)(P_0 >> 8);
		}
		if (P_1 > 8)
		{
			array[8] = (byte)(P_0 >> 16);
		}
		if (P_1 > 9)
		{
			array[9] = (byte)(P_0 >> 24);
		}
		if (P_1 > 10)
		{
			array[10] = (byte)(P_0 >> 32);
		}
		if (P_1 > 11)
		{
			array[11] = (byte)(P_0 >> 40);
		}
		if (P_1 > 12)
		{
			array[12] = (byte)(P_0 >> 48);
		}
		if (P_1 > 13)
		{
			array[13] = (byte)(P_0 >> 56);
		}
		if (P_1 > 14)
		{
			array[14] = 144;
		}
		if (P_1 > 15)
		{
			array[15] = 144;
		}
		if (P_1 > 16)
		{
			array[16] = 144;
		}
		if (P_1 > 17)
		{
			array[17] = 144;
		}
		if (P_1 > 18)
		{
			array[18] = 144;
		}
		if (P_1 > 19)
		{
			array[19] = 144;
		}
		return array;
	}

	private void InstallNewsPreview()
	{
		InstallProfileHooks();
	}

	private void RestoreNewsPreview()
	{
		RestoreProfileHooks();
	}
}
