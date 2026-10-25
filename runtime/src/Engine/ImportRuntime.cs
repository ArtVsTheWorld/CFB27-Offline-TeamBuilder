using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using OfflineTeamBuilder.Repair;

internal static class CFB27TeamBuilderBatchQueuePreMcrProbe
{
	internal static Action BeforeRestore;
	internal static Func<IntPtr, ulong, ulong, bool> IsOwnedAssetChain;
	private sealed class HookSpec
	{
		public string Name;

		public long Rva;

		public byte[] Expected;

		public int PatchLen;

		public long ResolvedTarget;

		public long ResolvedRva;

		public string ValidationNote;

		public byte[] Original;

		public long Remote;

		public long Data;

		public string Kind;

		public HookSpec(string name, long rva, string expectedHex, string kind)
		{
			var spec = Array.Find(OfflineTeamBuilderRuntime.GameProfile.Current.transport, x => x.name == name);
			if (spec != null) { rva = OfflineTeamBuilderRuntime.GameProfile.Parse(spec.rva); expectedHex = spec.expected; kind = spec.kind; }
			Name = name;
			Rva = rva;
			Expected = OfflineTeamBuilderRuntime.GameProfile.Hex(expectedHex);
			PatchLen = Expected.Length;
			Kind = kind;
		}
	}

	private struct MBI64
	{
		public ulong BaseAddress;

		public ulong AllocationBase;

		public uint AllocationProtect;

		public uint Alignment1;

		public ulong RegionSize;

		public uint State;

		public uint Protect;

		public uint Type;

		public uint Alignment2;
	}

	private sealed class LifecycleWatchSession
	{
		public volatile bool StopRequested;

		public Thread Worker;

		public string RootDirectory;

		public int TransientGenerations;

		public ulong LastTransientPointer;

		public uint LastTransientState;

		public bool OwnerSeen;

		public ulong OwnerPointer;

		public ulong OwnerFirstVtable;

		public ulong OwnerLastVtable;

		public long OwnerFirstSeenMs;

		public long OwnerLastSeenMs;

		public int OwnerVtableChanges;
	}

	private sealed class BatchQueueWatchSession
	{
		public volatile bool StopRequested;

		public Thread Worker;

		public string RootDirectory;

		public uint LastCount;

		public ulong Slot1;

		public ulong Slot2;

		public ulong Slot3;

		public int Slot1Generations;

		public int Slot2Generations;

		public int Slot3Generations;
	}

	private sealed class CurrentItemWatchSession
	{
		public volatile bool StopRequested;

		public Thread Worker;

		public string RootDirectory;

		public ulong TransientPointer;

		public uint CurrentTeamBuilderId;

		public uint RemainingItems;

		public uint TotalWorkUnits;

		public uint CompletedWorkUnits;

		public uint ActiveState;

		public ulong CurrentPointer70;

		public ulong CurrentPointer78;

		public ulong CurrentPointer80;

		public int ItemSwitches;

		public int CallbackBSnapshots;

		public bool PreMcrStallDetected;

		public uint StallTeamBuilderId;

		public long StallElapsedMs;
	}

	private sealed class ProtoHttpWatchSession
	{
		public volatile bool StopRequested;

		public Thread Worker;

		public string RootDirectory;

		public bool HandleSeen;

		public ulong OwnerPointer;

		public ulong HandlePointer;

		public bool FirstStateCaptured;

		public uint FirstMode;

		public uint LastMode;

		public int FirstRecvOffset;

		public int LastRecvOffset;

		public byte FirstFlag11EF;

		public byte LastFlag11EF;

		public ulong Backend08;

		public ulong Backend10;

		public ulong Field20;

		public ulong LastRecvCallCount;

		public int LastRecvResult;

		public bool RecvWaitSeen;

		public bool RecvFailSeen;

		public int HandleChanges;

		public string BaseUrlValue = "";

		public int BaseUrlLength;

		public string QueryHostValue = "";

		public int QueryHostLength;

		public string RequestLine = "";

		public string HostHeader = "";

		public int AuthorizationBearerLength;

		public int ErrorCodeB8;

		public uint ErrorCodeBC;

		public string ErrorText = "";

		public bool RequestHeaderCaptured;

		public bool EndpointFieldsCaptured;
	}

	private sealed class AdaptiveContextMatch
	{
		public long GlobalRva;

		public long UltimateCareer;

		public long Manager;

		public long Config;

		public uint Capacity;

		public uint Count;

		public int ManagerOffset;

		public int ConfigOffset;

		public int CapacityOffset;

		public string Mode;
	}

	private sealed class CodeCapacityAccess
	{
		public long GlobalRva;

		public int ManagerOffset;

		public int ConfigOffset;

		public int CapacityOffset;

		public long GlobalLoadRva;

		public long ManagerLoadRva;

		public long ConfigLoadRva;

		public long CapacityLoadRva;

		public bool NearReturn;
	}

	private sealed class LiveCapacityGroup
	{
		public long UltimateCareer;

		public long Manager;

		public long Config;

		public long CapacityAddress;

		public uint Capacity;

		public int ManagerOffset;

		public int ConfigOffset;

		public int CapacityOffset;

		public int EvidenceCount;

		public int GlobalXrefs;

		public int Score;

		public long BestGlobalRva;

		public List<long> CodeRvas = new List<long>();
	}

	private sealed class NamedTraceRef
	{
		public long RefRva;

		public string Kind;

		public int Width;
	}

	private sealed class CapacityVtablePatch
	{
		public long SingletonGlobalRva;

		public long Singleton;

		public long Vtable;

		public long SlotAddress;

		public long OriginalTarget;

		public long StubAddress;

		public long AddTeamsTarget;
	}

	private sealed class ReloadVtablePatch
	{
		public long SingletonGlobalRva;

		public long Singleton;

		public long Vtable;

		public long SlotAddress;

		public long OriginalReloadTarget;

		public long LoadTarget;

		public long StubAddress;

		public long CounterAddress;
	}

	private sealed class DurablePairRepairPatch
	{
		public long SingletonGlobalRva;

		public long Vtable;

		public long CareerSlotAddress;

		public long AssetKeySlotAddress;

		public long OriginalCareerTarget;

		public long OriginalAssetKeyTarget;

		public long LoadTarget;

		public long ReloadTarget;

		public long CareerStubAddress;

		public long AssetKeyStubAddress;

		public long StatusAddress;
	}

	private const uint PROCESS_VM_READ = 16u;

	private const uint PROCESS_VM_WRITE = 32u;

	private const uint PROCESS_VM_OPERATION = 8u;

	private const uint PROCESS_QUERY_INFORMATION = 1024u;

	private const uint MEM_COMMIT = 4096u;

	private const uint MEM_RESERVE = 8192u;

	private const uint PAGE_EXECUTE_READWRITE = 64u;

	private const long RVA_ULTIMATE_CAREER_GLOBAL = 259972648L;

	private const long RVA_KEY_GATE = 59751009L;

	private const long RVA_LOOKUP_RESULT = 59751321L;

	private const long RVA_OWNER_HELPER = 51239152L;

	private const long RVA_OWNER_GATE1_RESULT = 51239190L;

	private const long RVA_OWNER_GATE2_RESULT = 51239211L;

	private const long RVA_OWNER_GATE3_RESULT = 51239236L;

	private const long RVA_OWNER_ALLOC_RESULT = 51239297L;

	private const long RVA_OWNER_CTOR = 51212224L;

	private const long RVA_OWNER_HELPER_RETURN = 59751385L;

	private const long RVA_CALLBACK_A = 45046000L;

	private const long RVA_CALLBACK_B = 45046144L;

	private const long RVA_CALLBACK_C = 45046864L;

	private const long RVA_CALLBACK_C_DISPATCH_WRAPPER = 45024720L;

	private const long RVA_FAILURE_HELPER = 59752737L;

	private const long RVA_CALLBACK_B_RETURN = 45024664L;

	private const long RVA_CALLBACK_C_RETURN = 45024754L;

	private const long RVA_FAILURE_HELPER_RETURN = 59760215L;

	private const long RVA_MCR_FAILURE_COMPLETION = 59760144L;

	private const long RVA_MCR_COMPLETION_SIBLING_A = 59760272L;

	private const long RVA_MCR_COMPLETION_SIBLING_B = 59760512L;

	private const long RVA_MCR_HTTP_POLL = 51237440L;

	private const long RVA_MCR_HTTP_RECV_RESULT = 51237493L;

	private const long RVA_MCR_HTTP_STATUS_RESULT = 51237532L;

	private const long RVA_MCR_HTTP_SUCCESS_DECISION = 51237548L;

	private const long RVA_MCR_HTTP_FAILURE_DECISION = 51237641L;

	private const long RVA_MCR_HTTP_FAILURE_METHOD_ACTUAL = 51233296L;

	private const long RVA_PROTOHTTP_RECV = 23949024L;

	private const long RVA_PROTOHTTP_REQUEST = 23948720L;

	private const long RVA_PROTOHTTP_SEND = 23949856L;

	private const long RVA_MCR_REQUEST_START = 51227408L;

	private const long RVA_MCR_REQUEST_CONFIG_INIT = 51231392L;

	private const long RVA_MCR_SERVICE_STRING_GET = 51008816L;

	private const long RVA_MCR_HTTP_SUCCESS_METHOD_ACTUAL = 51236368L;

	private const long RVA_MCR_STAGE1_SUCCESS_CONTINUATION = 51225024L;

	private const long RVA_MCR_STAGE2_REQUEST_START = 51231904L;

	private const long RVA_MCR_STAGE2_SUCCESS_METHOD = 51236816L;

	private const long RVA_MCR_STAGE2_FAILURE_METHOD = 51233856L;

	private const long RVA_MCR_STAGE2_VTABLE = 186261384L;

	private const long RVA_MCR_LOCAL_URL_CALLSITE_OLD_SIBLING = 51227045L;

	private const long RVA_MCR_URL_FORMATTER = 51223664L;

	private const long RVA_MCR_URL_FORMAT = 186259792L;

	private const long RVA_MCR_AUTH_ACCESSOR = 50781952L;

	private const long RVA_MCR_API_INT = 182779232L;

	private const long RVA_MCR_QUERY_INT = 182779560L;

	private const long RVA_MCR_API_PROD = 182780384L;

	private const long RVA_MCR_QUERY_PROD = 182780488L;

	private const long RVA_MCR_API_LOAD = 182781304L;

	private const long RVA_MCR_QUERY_LOAD = 182781456L;

	private const long RVA_SERVICE_CONFIG_KEY_TABLE = 226020016L;

	private const long RVA_MCR_REQUEST_VTABLE = 186260600L;

	private const long RVA_DERIVED_MCR_OWNER_VTABLE = 186260464L;

	private const long RVA_MCR_CATEGORY_ONLINELEAGUE_TB_FILE = 186259584L;

	private const long RVA_MCR_CATEGORY_OFFLINELEAGUE_TB_FILE = 186259624L;

	private const uint TEST_CAPACITY = 16u;

	private const ulong SYNTHETIC_BLAZE_KEY = 1229782938247303441uL;

	private const ulong SYNTHETIC_ACCOUNT_ID = 2459565876494606882uL;

	internal static Action<string> ExternalLog;

	internal static Action<string> BootstrapLog;

	internal static volatile bool LastSessionReachedReady;

	private static string LocalLoopbackBaseUrl = "";

	private static int ActiveModuleSize = 0;

	private static long ResolvedUltimateCareerGlobalRva = 259972648L;

	private static long StringRelocationDeltaHint = 0L;

	private static long ResolvedKeyGateRva = 0L;

	private static int ResolvedManagerOffset = 1128;

	private static int ResolvedConfigOffset = 488;

	private static int ResolvedCapacityOffset = 76;

	private static volatile bool PersistentCancelRequested;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, IntPtr size, out IntPtr bytesRead);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr bytesWritten);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, UIntPtr size, uint allocationType, uint protect);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool VirtualProtectEx(IntPtr process, IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FlushInstructionCache(IntPtr process, IntPtr baseAddress, UIntPtr size);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern int VirtualQueryEx(IntPtr process, IntPtr address, out MBI64 buffer, uint length);

	[DllImport("kernel32.dll")]
	private static extern bool CloseHandle(IntPtr handle);

	private static byte[] Hex(string text)
	{
		string[] array = text.Split(new char[1] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		byte[] array2 = new byte[array.Length];
		for (int i = 0; i < array.Length; i++)
		{
			array2[i] = byte.Parse(array[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
		}
		return array2;
	}

	private static string H(long value)
	{
		return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
	}

	private static string HU(ulong value)
	{
		return "0x" + value.ToString("X", CultureInfo.InvariantCulture);
	}

	private static void B(string line)
	{
		Action<string> bootstrapLog = BootstrapLog;
		if (bootstrapLog != null)
		{
			try
			{
				bootstrapLog(line);
			}
			catch
			{
			}
		}
	}

	private static string BytesHex(byte[] bytes)
	{
		if (bytes == null)
		{
			return "<null>";
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < bytes.Length; i++)
		{
			if (i != 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	private static byte[] Read(IntPtr handle, long address, int length, out int error)
	{
		byte[] array = new byte[length];
		IntPtr bytesRead;
		bool flag = ReadProcessMemory(handle, new IntPtr(address), array, new IntPtr(length), out bytesRead);
		error = ((!flag) ? Marshal.GetLastWin32Error() : 0);
		int num = (int)bytesRead.ToInt64();
		if (num == length)
		{
			return array;
		}
		if (num < 0)
		{
			num = 0;
		}
		Array.Resize(ref array, num);
		return array;
	}

	private static string ReadAsciiZ(IntPtr handle, long address, int maxLength, out int error)
	{
		byte[] array = Read(handle, address, maxLength, out error);
		int i;
		for (i = 0; i < array.Length && array[i] != 0; i++)
		{
		}
		return Encoding.ASCII.GetString(array, 0, i);
	}

	private static bool Write(IntPtr handle, long address, byte[] bytes, out int error)
	{
		IntPtr bytesWritten;
		bool flag = WriteProcessMemory(handle, new IntPtr(address), bytes, new IntPtr(bytes.Length), out bytesWritten);
		error = ((!flag) ? Marshal.GetLastWin32Error() : 0);
		if (flag)
		{
			return bytesWritten.ToInt64() == bytes.Length;
		}
		return false;
	}

	private static bool ReadU64(IntPtr handle, long address, out ulong value, out int error)
	{
		value = 0uL;
		byte[] array = Read(handle, address, 8, out error);
		if (array.Length != 8)
		{
			return false;
		}
		value = BitConverter.ToUInt64(array, 0);
		return true;
	}

	private static bool ReadU32(IntPtr handle, long address, out uint value, out int error)
	{
		value = 0u;
		byte[] array = Read(handle, address, 4, out error);
		if (array.Length != 4)
		{
			return false;
		}
		value = BitConverter.ToUInt32(array, 0);
		return true;
	}

	private static bool WriteU32(IntPtr handle, long address, uint value, out int error)
	{
		return Write(handle, address, BitConverter.GetBytes(value), out error);
	}

	private static bool Query(IntPtr handle, long address, out MBI64 mbi)
	{
		return VirtualQueryEx(handle, new IntPtr(address), out mbi, (uint)Marshal.SizeOf(typeof(MBI64))) != 0;
	}

	private static bool Readable(IntPtr handle, long address)
	{
		if (!Query(handle, address, out var mbi) || mbi.State != 4096)
		{
			return false;
		}
		uint num = mbi.Protect & 0xFF;
		switch (num)
		{
		case 1u:
			return false;
		default:
			return num == 128;
		case 2u:
		case 4u:
		case 8u:
		case 16u:
		case 32u:
		case 64u:
			return true;
		}
	}

	private static void Add(List<byte> list, params byte[] bytes)
	{
		list.AddRange(bytes);
	}

	private static void Imm64(List<byte> list, ulong value)
	{
		list.AddRange(BitConverter.GetBytes(value));
	}

	private static int EmitRel8Placeholder(List<byte> list, byte opcode)
	{
		list.Add(opcode);
		int count = list.Count;
		list.Add(0);
		return count;
	}

	private static void PatchRel8(List<byte> list, int displacementIndex, int targetIndex)
	{
		int num = displacementIndex + 1;
		int num2 = targetIndex - num;
		if (num2 < -128 || num2 > 127)
		{
			throw new InvalidOperationException("rel8 out of range");
		}
		list[displacementIndex] = (byte)(sbyte)num2;
	}

	private static int EmitRel32Placeholder(List<byte> list, byte opcode1, byte opcode2)
	{
		list.Add(opcode1);
		list.Add(opcode2);
		int count = list.Count;
		list.Add(0);
		list.Add(0);
		list.Add(0);
		list.Add(0);
		return count;
	}

	private static int EmitRel32Placeholder(List<byte> list, byte opcode)
	{
		list.Add(opcode);
		int count = list.Count;
		list.Add(0);
		list.Add(0);
		list.Add(0);
		list.Add(0);
		return count;
	}

	private static void PatchRel32(List<byte> list, int displacementIndex, int targetIndex)
	{
		int num = displacementIndex + 4;
		byte[] bytes = BitConverter.GetBytes(targetIndex - num);
		for (int i = 0; i < 4; i++)
		{
			list[displacementIndex + i] = bytes[i];
		}
	}

	private static bool IsReadableProtection(uint protect)
	{
		uint num = protect & 0xFF;
		switch (num)
		{
		case 0u:
		case 1u:
			return false;
		default:
			return num == 128;
		case 2u:
		case 4u:
		case 8u:
		case 16u:
		case 32u:
		case 64u:
			return true;
		}
	}

	private static bool HookByteMatches(HookSpec spec, int index, byte actual)
	{
		if (spec.Kind == "key" && index >= 5 && index <= 8)
		{
			return true;
		}
		if (spec.Kind == "gate3" && ((index >= 4 && index <= 7) || (index >= 14 && index <= 17) || (index >= 27 && index <= 30)))
		{
			return true;
		}
		return actual == spec.Expected[index];
	}

	private static bool HookMatches(HookSpec spec, byte[] buffer, int offset)
	{
		if (buffer == null || offset < 0 || offset + spec.PatchLen > buffer.Length)
		{
			return false;
		}
		for (int i = 0; i < spec.PatchLen; i++)
		{
			if (!HookByteMatches(spec, i, buffer[offset + i]))
			{
				return false;
			}
		}
		return true;
	}

	private static bool ExactMatches(byte[] buffer, int offset, byte[] pattern)
	{
		if (buffer == null || pattern == null || offset < 0 || offset + pattern.Length > buffer.Length)
		{
			return false;
		}
		for (int i = 0; i < pattern.Length; i++)
		{
			if (buffer[offset + i] != pattern[i])
			{
				return false;
			}
		}
		return true;
	}

	private static List<long> FindPatternRvasInModule(IntPtr handle, long moduleBase, int moduleSize, byte[] pattern, HookSpec hookSpec, int maxHits, out int error)
	{
		List<long> list = new List<long>();
		error = 0;
		if (pattern == null || pattern.Length == 0 || moduleSize <= 0)
		{
			return list;
		}
		long num = moduleBase + moduleSize;
		long num2 = moduleBase;
		int num3 = ((pattern.Length > 1) ? (pattern.Length - 1) : 0);
		while (num2 < num && list.Count < maxHits)
		{
			if (!Query(handle, num2, out var mbi))
			{
				error = Marshal.GetLastWin32Error();
				break;
			}
			long baseAddress = (long)mbi.BaseAddress;
			long num4;
			try
			{
				num4 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num4 = num;
			}
			if (num4 <= num2)
			{
				num4 = num2 + 4096;
			}
			long num5 = Math.Max(num2, baseAddress);
			long num6 = Math.Min(num, num4);
			if (mbi.State == 4096 && IsReadableProtection(mbi.Protect) && num6 > num5)
			{
				int num8;
				for (long num7 = num5; num7 < num6; num7 += num8 - num3)
				{
					if (list.Count >= maxHits)
					{
						break;
					}
					num8 = (int)Math.Min(2097152L, num6 - num7);
					byte[] array = Read(handle, num7, num8, out error);
					if (array.Length >= pattern.Length)
					{
						for (int i = 0; i + pattern.Length <= array.Length; i++)
						{
							if (list.Count >= maxHits)
							{
								break;
							}
							if ((hookSpec == null) ? ExactMatches(array, i, pattern) : HookMatches(hookSpec, array, i))
							{
								long num9 = num7 + i - moduleBase;
								if (list.Count == 0 || list[list.Count - 1] != num9)
								{
									list.Add(num9);
								}
							}
						}
					}
					if (num8 <= num3)
					{
						break;
					}
				}
			}
			num2 = Math.Max(num2 + 4096, num4);
		}
		return list;
	}

	private static string RvaList(List<long> hits)
	{
		if (hits == null || hits.Count == 0)
		{
			return "<none>";
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < hits.Count; i++)
		{
			if (i != 0)
			{
				stringBuilder.Append(", ");
			}
			stringBuilder.Append("0x");
			stringBuilder.Append(hits[i].ToString("X", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	private static bool ResolveStaticString(IntPtr handle, long moduleBase, int moduleSize, long nominalRva, string expected, out long resolvedRva, out string note, out int error)
	{
		resolvedRva = nominalRva;
		note = "";
		string text = ReadAsciiZ(handle, moduleBase + nominalRva, Math.Max(64, expected.Length + 8), out error);
		if (text == expected)
		{
			note = "nominal matched";
			return true;
		}
		byte[] bytes = Encoding.ASCII.GetBytes(expected);
		List<long> list = FindPatternRvasInModule(handle, moduleBase, moduleSize, bytes, null, 16, out error);
		if (list.Count == 1)
		{
			resolvedRva = list[0];
			note = "relocated unique exact string; delta=" + (resolvedRva - nominalRva).ToString("+#;-#;0", CultureInfo.InvariantCulture);
			return true;
		}
		note = "nominal value=[" + text + "]; full-module exact-string hits=" + list.Count.ToString(CultureInfo.InvariantCulture) + " at " + RvaList(list);
		return false;
	}

	private static bool MatchMaskedContext(byte[] buffer, int offset, byte[] expected, int[] wildcardIndexes)
	{
		if (buffer == null || expected == null || offset < 0 || offset + expected.Length > buffer.Length)
		{
			return false;
		}
		for (int i = 0; i < expected.Length; i++)
		{
			bool flag = false;
			if (wildcardIndexes != null)
			{
				for (int j = 0; j < wildcardIndexes.Length; j++)
				{
					if (wildcardIndexes[j] == i)
					{
						flag = true;
						break;
					}
				}
			}
			if (!flag && buffer[offset + i] != expected[i])
			{
				return false;
			}
		}
		return true;
	}

	private static bool TryResolveOwnerGate3ByParentXref(IntPtr handle, long moduleBase, List<long> structural, out long selectedRva, out string note, out int error)
	{
		selectedRva = 0L;
		note = "";
		error = 0;
		if (structural == null || structural.Count == 0)
		{
			note = "no structurally safe owner-helper candidates were available for parent-xref resolution";
			return false;
		}
		if (ResolvedKeyGateRva == 0L)
		{
			note = "KEY_GATE had not been resolved before owner-helper parent-xref resolution";
			error = 32764;
			return false;
		}
		long num = 376L;
		long num2 = 84L;
		long num3 = ResolvedKeyGateRva + num;
		long num4 = num3 - 5;
		StringBuilder stringBuilder = new StringBuilder();
		List<long> list = new List<long>();
		byte[] array = Read(handle, moduleBase + num4, 5, out error);
		if (array.Length == 5 && array[0] == 232)
		{
			long num5 = num4 + 5 + BitConverter.ToInt32(array, 1);
			long num6 = num5 + num2;
			bool flag = structural.Contains(num6);
			stringBuilder.Append("exactParentCall=0x");
			stringBuilder.Append(num4.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(" -> helper=0x");
			stringBuilder.Append(num5.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(" -> gate3=0x");
			stringBuilder.Append(num6.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(flag ? " MATCH" : " no-match");
			if (flag)
			{
				selectedRva = num6;
				note = "owner-helper ambiguity resolved by exact relocated parent xref from the KEY_GATE function; " + stringBuilder.ToString();
				return true;
			}
		}
		else
		{
			stringBuilder.Append("exactParentCall=0x");
			stringBuilder.Append(num4.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(" was not a usable E8 call");
		}
		long num7 = num4 - 256;
		byte[] array2 = Read(handle, moduleBase + num7, 517, out error);
		if (array2.Length >= 5)
		{
			for (int i = 0; i + 5 <= array2.Length; i++)
			{
				if (array2[i] != 232)
				{
					continue;
				}
				long num8 = num7 + i;
				long num9 = num8 + 5 + BitConverter.ToInt32(array2, i + 1);
				long item = num9 + num2;
				if (structural.Contains(item))
				{
					if (stringBuilder.Length != 0)
					{
						stringBuilder.Append("; ");
					}
					stringBuilder.Append("xrefCall=0x");
					stringBuilder.Append(num8.ToString("X", CultureInfo.InvariantCulture));
					stringBuilder.Append(" deltaFromExpected=");
					stringBuilder.Append((num8 - num4).ToString("+#;-#;0", CultureInfo.InvariantCulture));
					stringBuilder.Append(" -> helper=0x");
					stringBuilder.Append(num9.ToString("X", CultureInfo.InvariantCulture));
					stringBuilder.Append(" -> gate3=0x");
					stringBuilder.Append(item.ToString("X", CultureInfo.InvariantCulture));
					if (!list.Contains(item))
					{
						list.Add(item);
					}
				}
			}
		}
		if (list.Count == 1)
		{
			selectedRva = list[0];
			note = "owner-helper ambiguity resolved by relocated KEY_GATE parent-function xref within +/-0x100 of the proven old callsite; " + stringBuilder.ToString();
			return true;
		}
		note = "KEY_GATE parent-xref discriminator produced " + list.Count.ToString(CultureInfo.InvariantCulture) + " candidate(s). expected parent return RVA=0x" + num3.ToString("X", CultureInfo.InvariantCulture) + " expected call RVA=0x" + num4.ToString("X", CultureInfo.InvariantCulture) + ". " + stringBuilder.ToString();
		error = 32763;
		return false;
	}

	private static bool TryResolveOwnerGate3ByFullChain(IntPtr handle, long moduleBase, HookSpec spec, List<long> hits, out long selectedRva, out string note, out int error)
	{
		selectedRva = 0L;
		note = "";
		error = 0;
		if (hits == null || hits.Count == 0)
		{
			return false;
		}
		byte[] expected = Hex("48 8B C8 48 85 C0 0F 84 00 00 00 00 48 8B 00 FF 90 80 02 00 00");
		int[] wildcardIndexes = new int[4] { 8, 9, 10, 11 };
		byte[] expected2 = Hex("48 85 C0 0F 84 00 00 00 00 45 33 C0 BA AC 00 00 00 48 8B C8 E8 00 00 00 00");
		int[] wildcardIndexes2 = new int[8] { 5, 6, 7, 8, 21, 22, 23, 24 };
		List<long> list = new List<long>();
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < hits.Count; i++)
		{
			long num = hits[i];
			long num2 = num - 46;
			long num3 = num - 25;
			byte[] array = Read(handle, moduleBase + num2, 46 + spec.PatchLen, out error);
			bool flag = array.Length == 46 + spec.PatchLen && MatchMaskedContext(array, 0, expected, wildcardIndexes) && MatchMaskedContext(array, 21, expected2, wildcardIndexes2) && HookMatches(spec, array, 46);
			bool flag2 = false;
			long num4 = 0L;
			if (flag)
			{
				long num5 = num2 + 12 + BitConverter.ToInt32(array, 8);
				long num6 = num3 + 9 + BitConverter.ToInt32(array, 26);
				long num7 = num + 8 + BitConverter.ToInt32(array, 50);
				long num8 = num + 18 + BitConverter.ToInt32(array, 60);
				long num9 = num + 31 + BitConverter.ToInt32(array, 73);
				flag2 = num5 == num6 && num5 == num7 && num5 == num8 && num5 == num9;
				if (flag2)
				{
					num4 = num5;
				}
			}
			if (stringBuilder.Length != 0)
			{
				stringBuilder.Append("; ");
			}
			stringBuilder.Append("0x");
			stringBuilder.Append(num.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(flag ? " prelude=PASS" : " prelude=FAIL");
			if (flag)
			{
				stringBuilder.Append(flag2 ? " branches=CONVERGE" : " branches=DIVERGE");
				if (flag2)
				{
					stringBuilder.Append(" fail=0x");
					stringBuilder.Append(num4.ToString("X", CultureInfo.InvariantCulture));
					stringBuilder.Append(" failDelta=");
					stringBuilder.Append((num4 - num).ToString("+#;-#;0", CultureInfo.InvariantCulture));
				}
			}
			if (flag && flag2)
			{
				list.Add(num);
			}
		}
		if (list.Count == 1)
		{
			selectedRva = list[0];
			note = "owner-helper ambiguity resolved by complete owner-helper chain: gate1 [rax+0x280], gate2 service id 0xAC, and five convergent failure branches. candidates: " + stringBuilder.ToString();
			return true;
		}
		if (list.Count > 1)
		{
			if (TryResolveOwnerGate3ByParentXref(handle, moduleBase, list, out var selectedRva2, out var note2, out var error2))
			{
				selectedRva = selectedRva2;
				note = "complete owner-helper structure left " + list.Count.ToString(CultureInfo.InvariantCulture) + " sibling candidates; " + note2 + ". structural candidates: " + stringBuilder.ToString();
				error = 0;
				return true;
			}
			note = "owner-helper structural discriminator produced " + list.Count.ToString(CultureInfo.InvariantCulture) + " safe sibling candidates; parent-xref resolution also failed: " + note2 + ". structural candidates: " + stringBuilder.ToString();
			error = error2;
			return false;
		}
		note = "owner-helper structural discriminator produced 0 safe candidates. candidates: " + stringBuilder.ToString();
		error = 32765;
		return false;
	}

	private static bool TryResolveProtoHttpRequestByMcrXref(IntPtr handle, long moduleBase, List<long> candidates, out long selectedRva, out string note, out int error)
	{
		selectedRva = 0L;
		note = "";
		error = 0;
		if (candidates == null || candidates.Count < 2)
		{
			note = "ProtoHttp MCR-xref resolver requires at least two vetted entry-signature candidates.";
			error = 32545;
			return false;
		}
		if (StringRelocationDeltaHint == 0L)
		{
			note = "No shared TeamBuilder static-data relocation delta is available for the MCR vtable anchor.";
			error = 32546;
			return false;
		}
		long num = 186260464 + StringRelocationDeltaHint;
		long num2 = 186260600 + StringRelocationDeltaHint;
		if (!ReadU64(handle, moduleBase + num + 8, out var value, out error) || value == 0L)
		{
			note = "Could not read relocated TeamBuilder request-start pointer at owner-vtable+0x08. vtableRVA=0x" + num.ToString("X", CultureInfo.InvariantCulture) + ".";
			return false;
		}
		if (!ReadU64(handle, moduleBase + num2 + 8, out var value2, out error) || value2 == 0L)
		{
			note = "Could not read relocated adjacent MCR request-start pointer at sibling-vtable+0x08. vtableRVA=0x" + num2.ToString("X", CultureInfo.InvariantCulture) + ".";
			return false;
		}
		long num3 = (long)value;
		long num4 = (long)value2;
		long num5 = moduleBase + ActiveModuleSize;
		if (num3 < moduleBase || num3 >= num5 || num4 < moduleBase || num4 >= num5)
		{
			note = "Relocated MCR vtable entries did not both point inside CollegeFB27.exe. active=" + H(num3) + " sibling=" + H(num4) + " activeVtableRVA=0x" + num.ToString("X", CultureInfo.InvariantCulture) + " siblingVtableRVA=0x" + num2.ToString("X", CultureInfo.InvariantCulture) + ".";
			error = 32547;
			return false;
		}
		long num6 = num4 - num3;
		if (num6 <= 0 || num6 > 12288)
		{
			num6 = 4608L;
		}
		int num7 = (int)num6;
		byte[] array = Read(handle, num3, num7, out error);
		if (array.Length != num7)
		{
			note = "Could not read relocated TeamBuilder request-start body. activeStart=" + H(num3) + " requested=0x" + num7.ToString("X", CultureInfo.InvariantCulture) + " got=0x" + array.Length.ToString("X", CultureInfo.InvariantCulture) + ".";
			return false;
		}
		HashSet<long> hashSet = new HashSet<long>();
		for (int i = 0; i < candidates.Count; i++)
		{
			hashSet.Add(candidates[i]);
		}
		Dictionary<long, List<long>> dictionary = new Dictionary<long, List<long>>();
		foreach (long candidate in candidates)
		{
			dictionary[candidate] = new List<long>();
		}
		for (int j = 0; j + 5 <= array.Length; j++)
		{
			if (array[j] == 232)
			{
				int num8 = BitConverter.ToInt32(array, j + 1);
				long num9 = num3 - moduleBase + j;
				long num10 = num9 + 5 + num8;
				if (hashSet.Contains(num10))
				{
					dictionary[num10].Add(num9);
				}
			}
		}
		List<long> list = new List<long>();
		StringBuilder stringBuilder = new StringBuilder();
		for (int k = 0; k < candidates.Count; k++)
		{
			long num11 = candidates[k];
			List<long> list2 = dictionary[num11];
			if (list2.Count != 0)
			{
				list.Add(num11);
			}
			if (stringBuilder.Length != 0)
			{
				stringBuilder.Append("; ");
			}
			stringBuilder.Append("0x");
			stringBuilder.Append(num11.ToString("X", CultureInfo.InvariantCulture));
			stringBuilder.Append(" xrefs=");
			stringBuilder.Append(list2.Count.ToString(CultureInfo.InvariantCulture));
			if (list2.Count == 0)
			{
				continue;
			}
			stringBuilder.Append(" at ");
			for (int l = 0; l < list2.Count; l++)
			{
				if (l != 0)
				{
					stringBuilder.Append(",");
				}
				stringBuilder.Append("0x");
				stringBuilder.Append(list2[l].ToString("X", CultureInfo.InvariantCulture));
			}
		}
		if (list.Count == 1)
		{
			selectedRva = list[0];
			note = "ProtoHttpRequest ambiguity resolved by direct xref from the relocated ACTUAL TeamBuilder MCR request-start function. ownerVtableRVA=0x" + num.ToString("X", CultureInfo.InvariantCulture) + " activeStartRVA=0x" + (num3 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " siblingVtableRVA=0x" + num2.ToString("X", CultureInfo.InvariantCulture) + " siblingStartRVA=0x" + (num4 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " selected=0x" + selectedRva.ToString("X", CultureInfo.InvariantCulture) + ". candidate xrefs: " + stringBuilder.ToString();
			return true;
		}
		note = "Relocated TeamBuilder MCR request-start xref discriminator referenced " + list.Count.ToString(CultureInfo.InvariantCulture) + " distinct ProtoHttp entry candidates; expected exactly one. ownerVtableRVA=0x" + num.ToString("X", CultureInfo.InvariantCulture) + " activeStartRVA=0x" + (num3 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " siblingVtableRVA=0x" + num2.ToString("X", CultureInfo.InvariantCulture) + " siblingStartRVA=0x" + (num4 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + ". candidate xrefs: " + stringBuilder.ToString();
		error = 32548;
		return false;
	}

	private static bool ResolveHookTarget(IntPtr handle, long moduleBase, HookSpec spec, out int error)
	{
		long num = moduleBase + spec.Rva;
		byte[] array = Read(handle, num, spec.PatchLen, out error);
		if (array.Length == spec.PatchLen && HookMatches(spec, array, 0))
		{
			spec.ResolvedTarget = num;
			spec.ResolvedRva = spec.Rva;
			spec.ValidationNote = "nominal masked signature matched; actual=[" + BytesHex(array) + "]";
			return true;
		}
		string text = BytesHex(array);
		byte[] array2 = Read(handle, num - 256, 512 + spec.PatchLen, out error);
		if (array2.Length >= spec.PatchLen)
		{
			List<int> list = new List<int>();
			for (int i = 0; i + spec.PatchLen <= array2.Length; i++)
			{
				if (HookMatches(spec, array2, i))
				{
					list.Add(i);
				}
			}
			if (list.Count == 1)
			{
				long num2 = (spec.ResolvedTarget = num - 256 + list[0]);
				spec.ResolvedRva = num2 - moduleBase;
				byte[] bytes = Read(handle, num2, spec.PatchLen, out error);
				spec.ValidationNote = "nominal mismatch; unique masked signature found within +/-0x100 at delta " + (num2 - num).ToString("+#;-#;0", CultureInfo.InvariantCulture) + ". actual=[" + BytesHex(bytes) + "]";
				return true;
			}
		}
		if (ActiveModuleSize > 0)
		{
			List<long> list2 = FindPatternRvasInModule(handle, moduleBase, ActiveModuleSize, spec.Expected, spec, 16, out error);
			if (list2.Count == 1)
			{
				spec.ResolvedRva = list2[0];
				spec.ResolvedTarget = moduleBase + spec.ResolvedRva;
				byte[] bytes2 = Read(handle, spec.ResolvedTarget, spec.PatchLen, out error);
				spec.ValidationNote = "post-patch full-module relocation; delta=" + (spec.ResolvedRva - spec.Rva).ToString("+#;-#;0", CultureInfo.InvariantCulture) + ". actual=[" + BytesHex(bytes2) + "]";
				return true;
			}
			if (spec.Kind == "gate3" && list2.Count > 1)
			{
				if (TryResolveOwnerGate3ByFullChain(handle, moduleBase, spec, list2, out var selectedRva, out var note, out error))
				{
					spec.ResolvedRva = selectedRva;
					spec.ResolvedTarget = moduleBase + selectedRva;
					byte[] bytes3 = Read(handle, spec.ResolvedTarget, spec.PatchLen, out error);
					spec.ValidationNote = note + " selectedRVA=0x" + selectedRva.ToString("X", CultureInfo.InvariantCulture) + " delta=" + (selectedRva - spec.Rva).ToString("+#;-#;0", CultureInfo.InvariantCulture) + " actual=[" + BytesHex(bytes3) + "]";
					return true;
				}
				spec.ValidationNote = "nominal mismatch; full-module masked-signature hits=" + list2.Count.ToString(CultureInfo.InvariantCulture) + " at " + RvaList(list2) + ". " + note + ". expected-template=[" + BytesHex(spec.Expected) + "] actual_at_nominal=[" + text + "]";
				return false;
			}
			if (spec.Kind == "protohttplocalurl" && list2.Count > 1)
			{
				if (TryResolveProtoHttpRequestByMcrXref(handle, moduleBase, list2, out var selectedRva2, out var note2, out error))
				{
					spec.ResolvedRva = selectedRva2;
					spec.ResolvedTarget = moduleBase + selectedRva2;
					byte[] bytes4 = Read(handle, spec.ResolvedTarget, spec.PatchLen, out error);
					spec.ValidationNote = note2 + " delta=" + (selectedRva2 - spec.Rva).ToString("+#;-#;0", CultureInfo.InvariantCulture) + " actual=[" + BytesHex(bytes4) + "]";
					return true;
				}
				spec.ValidationNote = "nominal mismatch; full-module masked-signature hits=" + list2.Count.ToString(CultureInfo.InvariantCulture) + " at " + RvaList(list2) + ". " + note2 + ". expected-template=[" + BytesHex(spec.Expected) + "] actual_at_nominal=[" + text + "]";
				return false;
			}
			spec.ValidationNote = "nominal mismatch; full-module masked-signature hits=" + list2.Count.ToString(CultureInfo.InvariantCulture) + " at " + RvaList(list2) + ". expected-template=[" + BytesHex(spec.Expected) + "] actual_at_nominal=[" + text + "]";
			error = 32766;
			return false;
		}
		spec.ValidationNote = "nominal mismatch and module size unavailable. expected-template=[" + BytesHex(spec.Expected) + "] actual_at_nominal=[" + text + "]";
		error = 32767;
		return false;
	}

	private static bool TryLocateActiveMcrUrlCallsite(IntPtr handle, long moduleBase, out long callsiteRva, out byte[] signature, out string note, out int error)
	{
		callsiteRva = 0L;
		signature = new byte[0];
		note = "";
		error = 0;
		if (!ReadU64(handle, moduleBase + 186260464 + 8, out var value, out error) || value == 0L)
		{
			note = "Could not read active TeamBuilder request-start pointer from vtable B1A1BF0+0x08.";
			return false;
		}
		if (!ReadU64(handle, moduleBase + 186260600 + 8, out var value2, out error) || value2 == 0L)
		{
			note = "Could not read adjacent request-start pointer from vtable B1A1C78+0x08.";
			return false;
		}
		long num = (long)value;
		long num2 = (long)value2;
		long num3 = num2 - num;
		if (num3 <= 0 || num3 > 8192)
		{
			num3 = 2048L;
		}
		int num4 = (int)num3;
		byte[] array = Read(handle, num, num4, out error);
		if (array.Length != num4)
		{
			note = "Could not read active request function body. activeStart=" + H(num) + " requested=0x" + num4.ToString("X", CultureInfo.InvariantCulture) + " got=0x" + array.Length.ToString("X", CultureInfo.InvariantCulture);
			return false;
		}
		long num5 = moduleBase + 186259792;
		long num6 = moduleBase + 51223664;
		List<int> list = new List<int>();
		for (int i = 0; i + 15 <= array.Length; i++)
		{
			if (array[i] != 72 || array[i + 1] != 141 || array[i + 2] != 21)
			{
				continue;
			}
			int num7 = BitConverter.ToInt32(array, i + 3);
			if (num + i + 7 + num7 == num5 && array[i + 7] == 72 && array[i + 8] == 139 && array[i + 9] == 206 && array[i + 10] == 232)
			{
				int num8 = BitConverter.ToInt32(array, i + 11);
				if (num + i + 15 + num8 == num6)
				{
					list.Add(i);
				}
			}
		}
		if (list.Count != 1)
		{
			note = "Active request URL locator matches=" + list.Count.ToString(CultureInfo.InvariantCulture) + " activeStartRVA=0x" + (num - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " adjacentStartRVA=0x" + (num2 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + ". Expected exactly one LEA(\"%s/api/\") + mov rcx,rsi + call formatter sequence.";
			error = 30465;
			return false;
		}
		int num9 = list[0];
		long num10 = num + num9;
		callsiteRva = num10 - moduleBase;
		signature = new byte[15];
		Buffer.BlockCopy(array, num9, signature, 0, 15);
		note = "Located from ACTUAL TeamBuilder request vtable +0x08. activeStartRVA=0x" + (num - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " adjacentStartRVA=0x" + (num2 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " urlCallsiteRVA=0x" + callsiteRva.ToString("X", CultureInfo.InvariantCulture) + " signature=[" + BytesHex(signature) + "]";
		return true;
	}

	private static byte[] BuildGenericTraceShell(long data, long resume, byte[] displaced)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 76, 139, 92, 36, 112);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 76, 137, 88, 8);
		Add(list, 72, 137, 72, 16);
		Add(list, 72, 137, 80, 24);
		Add(list, 76, 137, 64, 32);
		Add(list, 76, 137, 72, 40);
		Add(list, 72, 137, 88, 48);
		Add(list, 72, 137, 104, 56);
		Add(list, 72, 137, 112, 64);
		Add(list, 72, 137, 120, 72);
		Add(list, 76, 137, 96, 80);
		Add(list, 76, 137, 104, 88);
		Add(list, 76, 137, 112, 96);
		Add(list, 76, 137, 120, 104);
		Add(list, 76, 141, 148, 36, 128, 0, 0, 0);
		Add(list, 76, 137, 80, 112);
		Add(list, 77, 139, 26);
		Add(list, 76, 137, 88, 120);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, displaced);
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildOwnerCategoryShimShell(long data, long resume, byte[] displaced, long onlineCategory, long offlineCategory)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 76, 139, 92, 36, 112);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 76, 137, 88, 8);
		Add(list, 72, 137, 72, 16);
		Add(list, 72, 137, 80, 24);
		Add(list, 76, 137, 64, 32);
		Add(list, 76, 137, 72, 40);
		Add(list, 72, 137, 88, 48);
		Add(list, 72, 137, 104, 56);
		Add(list, 72, 137, 112, 64);
		Add(list, 72, 137, 120, 72);
		Add(list, 76, 137, 96, 80);
		Add(list, 76, 137, 104, 88);
		Add(list, 76, 137, 112, 96);
		Add(list, 76, 137, 120, 104);
		Add(list, 76, 141, 148, 36, 128, 0, 0, 0);
		Add(list, 76, 137, 80, 112);
		Add(list, 77, 139, 26);
		Add(list, 76, 137, 88, 120);
		Add(list, 76, 137, 128, 136, 0, 0, 0);
		Add(list, 73, 187);
		Imm64(list, (ulong)onlineCategory);
		Add(list, 77, 59, 195);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		Add(list, 72, 255, 128, 128, 0, 0, 0);
		Add(list, 73, 187);
		Imm64(list, (ulong)offlineCategory);
		Add(list, 76, 137, 156, 36, 56, 0, 0, 0);
		Add(list, 76, 137, 152, 136, 0, 0, 0);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, displaced);
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildCallbackCDispatchSourceShell(long data, long resume, byte[] displaced, long callbackTarget)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 76, 141, 156, 36, 128, 0, 0, 0);
		Add(list, 73, 139, 67, 40);
		Add(list, 72, 133, 192);
		int displacementIndex = EmitRel32Placeholder(list, 15, 132);
		Add(list, 72, 139, 0);
		Add(list, 72, 133, 192);
		int displacementIndex2 = EmitRel32Placeholder(list, 15, 132);
		Add(list, 76, 139, 16);
		Add(list, 72, 185);
		Imm64(list, (ulong)callbackTarget);
		Add(list, 73, 59, 202);
		int displacementIndex3 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 76, 139, 84, 36, 112);
		Add(list, 76, 137, 80, 8);
		Add(list, 76, 139, 84, 36, 104);
		Add(list, 76, 137, 80, 16);
		Add(list, 76, 139, 84, 36, 96);
		Add(list, 76, 137, 80, 24);
		Add(list, 76, 139, 84, 36, 56);
		Add(list, 76, 137, 80, 32);
		Add(list, 76, 139, 84, 36, 48);
		Add(list, 76, 137, 80, 40);
		Add(list, 76, 139, 84, 36, 88);
		Add(list, 76, 137, 80, 48);
		Add(list, 76, 139, 84, 36, 80);
		Add(list, 76, 137, 80, 56);
		Add(list, 76, 139, 84, 36, 72);
		Add(list, 76, 137, 80, 64);
		Add(list, 76, 139, 84, 36, 64);
		Add(list, 76, 137, 80, 72);
		Add(list, 76, 139, 84, 36, 24);
		Add(list, 76, 137, 80, 80);
		Add(list, 76, 139, 84, 36, 16);
		Add(list, 76, 137, 80, 88);
		Add(list, 76, 139, 84, 36, 8);
		Add(list, 76, 137, 80, 96);
		Add(list, 76, 139, 20, 36);
		Add(list, 76, 137, 80, 104);
		Add(list, 76, 141, 156, 36, 128, 0, 0, 0);
		Add(list, 76, 137, 88, 112);
		Add(list, 77, 139, 19);
		Add(list, 76, 137, 80, 120);
		Add(list, 77, 139, 83, 40);
		Add(list, 76, 137, 144, 128, 0, 0, 0);
		Add(list, 77, 139, 18);
		Add(list, 76, 137, 144, 136, 0, 0, 0);
		Add(list, 73, 139, 10);
		Add(list, 72, 137, 136, 144, 0, 0, 0);
		Add(list, 73, 139, 74, 16);
		Add(list, 72, 137, 136, 152, 0, 0, 0);
		Add(list, 73, 139, 75, 48);
		Add(list, 72, 137, 136, 160, 0, 0, 0);
		Add(list, 73, 139, 75, 56);
		Add(list, 72, 137, 136, 168, 0, 0, 0);
		Add(list, 73, 139, 75, 64);
		Add(list, 72, 137, 136, 176, 0, 0, 0);
		Add(list, 73, 139, 75, 72);
		Add(list, 72, 137, 136, 184, 0, 0, 0);
		Add(list, 252);
		Add(list, 73, 141, 179, 0, 0, 0, 0);
		Add(list, 72, 191);
		Imm64(list, (ulong)(data + 256));
		Add(list, 185, 64, 0, 0, 0);
		Add(list, 243, 72, 165);
		int count = list.Count;
		PatchRel32(list, displacementIndex, count);
		PatchRel32(list, displacementIndex2, count);
		PatchRel32(list, displacementIndex3, count);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, displaced);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildKeyShimShell(long data, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 72, 137, 112, 8);
		Add(list, 72, 137, 120, 32);
		Add(list, 72, 137, 104, 40);
		Add(list, 72, 133, 246);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		Add(list, 72, 255, 64, 24);
		Add(list, 72, 190);
		Imm64(list, 1229782938247303441uL);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 72, 137, 112, 16);
		Add(list, 88);
		Add(list, 157);
		Add(list, 68, 139, 140, 36, 224, 0, 0, 0);
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildLookupResultShell(long data, long resume, bool injectOwner)
	{
		List<byte> list = new List<byte>();
		Add(list, 72, 133, 192);
		int displacementIndex = EmitRel8Placeholder(list, 116);
		Add(list, 72, 139, 104, 72);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 156);
		Add(list, 82);
		Add(list, 72, 186);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 2);
		Add(list, 72, 137, 66, 8);
		Add(list, 72, 137, 106, 16);
		Add(list, 72, 137, 122, 40);
		if (injectOwner)
		{
			Add(list, 72, 133, 237);
			int displacementIndex2 = EmitRel8Placeholder(list, 117);
			Add(list, 72, 255, 66, 32);
			Add(list, 72, 189);
			Imm64(list, 2459565876494606882uL);
			int count2 = list.Count;
			PatchRel8(list, displacementIndex2, count2);
		}
		Add(list, 72, 137, 106, 24);
		Add(list, 90);
		Add(list, 157);
		Add(list, 76, 139, 132, 36, 232, 0, 0, 0);
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)resume);
		return list.ToArray();
	}

	private static void EmitAbsJump(List<byte> list, ulong target)
	{
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, target);
	}

	private static byte[] BuildOwnerGate1ResultShell(long data, long failTarget, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 81);
		Add(list, 72, 185);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 1);
		Add(list, 72, 137, 65, 8);
		Add(list, 89);
		Add(list, 157);
		Add(list, 72, 139, 200);
		Add(list, 72, 133, 192);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		EmitAbsJump(list, (ulong)failTarget);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 72, 139, 0);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildOwnerGate2ResultShell(long data, long failTarget, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 81);
		Add(list, 72, 185);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 1);
		Add(list, 72, 137, 65, 8);
		Add(list, 89);
		Add(list, 157);
		Add(list, 72, 133, 192);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		EmitAbsJump(list, (ulong)failTarget);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 69, 51, 192);
		Add(list, 186, 172, 0, 0, 0);
		Add(list, 72, 139, 200);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildOwnerGate3ResultShell(long data, long failTarget, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80);
		Add(list, 82);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 139, 84, 36, 8);
		Add(list, 72, 137, 80, 8);
		Add(list, 15, 182, 87, 8);
		Add(list, 72, 137, 80, 16);
		Add(list, 72, 139, 87, 16);
		Add(list, 72, 137, 80, 24);
		Add(list, 72, 137, 120, 32);
		Add(list, 72, 199, 64, 56, 0, 0, 0, 0);
		Add(list, 90);
		Add(list, 88);
		Add(list, 157);
		Add(list, 133, 192);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		Add(list, 80);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 64, 40);
		Add(list, 88);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 128, 127, 8, 0);
		int displacementIndex2 = EmitRel8Placeholder(list, 117);
		Add(list, 80);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 64, 48);
		Add(list, 88);
		int count2 = list.Count;
		PatchRel8(list, displacementIndex2, count2);
		Add(list, 72, 139, 127, 16);
		Add(list, 72, 133, 255);
		int displacementIndex3 = EmitRel8Placeholder(list, 117);
		Add(list, 80);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 199, 64, 56, 3, 0, 0, 0);
		Add(list, 88);
		EmitAbsJump(list, (ulong)failTarget);
		int count3 = list.Count;
		PatchRel8(list, displacementIndex3, count3);
		Add(list, 80);
		Add(list, 82);
		Add(list, 72, 139, 87, 8);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 137, 80, 64);
		Add(list, 72, 133, 210);
		int displacementIndex4 = EmitRel8Placeholder(list, 117);
		Add(list, 72, 199, 64, 56, 4, 0, 0, 0);
		Add(list, 90);
		Add(list, 88);
		EmitAbsJump(list, (ulong)failTarget);
		int count4 = list.Count;
		PatchRel8(list, displacementIndex4, count4);
		Add(list, 90);
		Add(list, 88);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildReadinessGateShimShell(long data, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80);
		Add(list, 82);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 15, 182, 87, 8);
		Add(list, 72, 137, 80, 8);
		Add(list, 72, 139, 87, 16);
		Add(list, 72, 137, 80, 16);
		Add(list, 72, 137, 120, 24);
		Add(list, 128, 127, 8, 0);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		Add(list, 72, 255, 64, 32);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 90);
		Add(list, 88);
		Add(list, 157);
		Add(list, 72, 139, 127, 16);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildAllocatorResultTraceShell(long data, long failTarget, long resume)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 81);
		Add(list, 72, 185);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 1);
		Add(list, 72, 137, 65, 8);
		Add(list, 72, 137, 121, 16);
		Add(list, 89);
		Add(list, 157);
		Add(list, 72, 139, 216);
		Add(list, 72, 133, 192);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		EmitAbsJump(list, (ulong)failTarget);
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 72, 139, 87, 8);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildMcrHttpRecvResultShell(long data, long moduleBase)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 255, 2);
		Add(list, 73, 137, 66, 8);
		Add(list, 73, 137, 90, 16);
		Add(list, 68, 139, 155, 96, 2, 0, 0);
		Add(list, 77, 137, 90, 24);
		Add(list, 76, 139, 91, 32);
		Add(list, 77, 137, 90, 32);
		Add(list, 76, 141, 156, 36, 128, 0, 0, 0);
		Add(list, 77, 137, 90, 40);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, 139, 248);
		Add(list, 131, 248, 253);
		int displacementIndex = EmitRel8Placeholder(list, 117);
		EmitAbsJump(list, (ulong)(moduleBase + 51237650));
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 133, 192);
		int displacementIndex2 = EmitRel8Placeholder(list, 121);
		EmitAbsJump(list, (ulong)(moduleBase + 51237641));
		int count2 = list.Count;
		PatchRel8(list, displacementIndex2, count2);
		EmitAbsJump(list, (ulong)(moduleBase + 51237512));
		return list.ToArray();
	}

	private static byte[] BuildMcrHttpStatusResultShell(long data, long moduleBase)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 255, 2);
		Add(list, 73, 137, 66, 8);
		Add(list, 73, 137, 90, 16);
		Add(list, 73, 137, 122, 24);
		Add(list, 68, 139, 155, 124, 3, 0, 0);
		Add(list, 77, 137, 90, 32);
		Add(list, 68, 139, 155, 96, 2, 0, 0);
		Add(list, 77, 137, 90, 40);
		Add(list, 76, 141, 156, 36, 128, 0, 0, 0);
		Add(list, 77, 137, 90, 48);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, 139, 200);
		Add(list, 61, 200, 0, 0, 0);
		int displacementIndex = EmitRel8Placeholder(list, 125);
		EmitAbsJump(list, (ulong)(moduleBase + 51237589));
		int count = list.Count;
		PatchRel8(list, displacementIndex, count);
		Add(list, 61, 44, 1, 0, 0);
		int displacementIndex2 = EmitRel8Placeholder(list, 124);
		EmitAbsJump(list, (ulong)(moduleBase + 51237581));
		int count2 = list.Count;
		PatchRel8(list, displacementIndex2, count2);
		EmitAbsJump(list, (ulong)(moduleBase + 51237548));
		return list.ToArray();
	}

	private static byte[] BuildMcrHttpFailureMethodShell(long data, long resume, byte[] displaced)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 255, 2);
		Add(list, 73, 137, 74, 8);
		Add(list, 76, 139, 25);
		Add(list, 77, 137, 90, 16);
		Add(list, 68, 139, 153, 96, 2, 0, 0);
		Add(list, 77, 137, 90, 24);
		Add(list, 68, 139, 153, 124, 3, 0, 0);
		Add(list, 77, 137, 90, 32);
		Add(list, 76, 139, 89, 32);
		Add(list, 77, 137, 90, 40);
		Add(list, 76, 139, 153, 152, 3, 0, 0);
		Add(list, 77, 137, 90, 48);
		Add(list, 68, 139, 153, 164, 3, 0, 0);
		Add(list, 77, 137, 90, 56);
		Add(list, 68, 139, 153, 168, 3, 0, 0);
		Add(list, 77, 137, 90, 64);
		Add(list, 68, 139, 153, 8, 4, 0, 0);
		Add(list, 77, 137, 90, 72);
		Add(list, 76, 141, 156, 36, 128, 0, 0, 0);
		Add(list, 77, 137, 90, 80);
		Add(list, 77, 139, 27);
		Add(list, 77, 137, 90, 88);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, displaced);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildMcrHttpFailureDecisionShell(long data, long resume, byte[] displaced)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80, 81, 82, 83, 85, 86, 87);
		Add(list, 65, 80, 65, 81, 65, 82, 65, 83, 65, 84, 65, 85, 65, 86, 65, 87);
		Add(list, 72, 184);
		Imm64(list, (ulong)data);
		Add(list, 72, 255, 0);
		Add(list, 72, 137, 88, 8);
		Add(list, 72, 137, 120, 16);
		Add(list, 72, 137, 72, 24);
		Add(list, 68, 139, 147, 96, 2, 0, 0);
		Add(list, 76, 137, 80, 32);
		Add(list, 68, 139, 147, 124, 3, 0, 0);
		Add(list, 76, 137, 80, 40);
		Add(list, 76, 139, 83, 32);
		Add(list, 76, 137, 80, 48);
		Add(list, 76, 139, 147, 152, 3, 0, 0);
		Add(list, 76, 137, 80, 56);
		Add(list, 68, 139, 147, 164, 3, 0, 0);
		Add(list, 76, 137, 80, 64);
		Add(list, 68, 139, 147, 168, 3, 0, 0);
		Add(list, 76, 137, 80, 72);
		Add(list, 68, 139, 147, 8, 4, 0, 0);
		Add(list, 76, 137, 80, 80);
		Add(list, 76, 139, 19);
		Add(list, 76, 137, 80, 88);
		Add(list, 77, 139, 82, 40);
		Add(list, 76, 137, 80, 96);
		Add(list, 76, 141, 148, 36, 128, 0, 0, 0);
		Add(list, 76, 137, 80, 104);
		Add(list, 77, 139, 26);
		Add(list, 76, 137, 88, 112);
		Add(list, 65, 95, 65, 94, 65, 93, 65, 92, 65, 91, 65, 90, 65, 89, 65, 88);
		Add(list, 95, 94, 93, 91, 90, 89, 88, 157);
		Add(list, displaced);
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildLocalUrlShimShell(long data, long resume, long formatString, long formatter)
	{
		List<byte> list = new List<byte>();
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 255, 2);
		Add(list, 77, 137, 66, 8);
		Add(list, 73, 184);
		Imm64(list, (ulong)(data + 512));
		Add(list, 77, 137, 66, 16);
		Add(list, 72, 186);
		Imm64(list, (ulong)formatString);
		Add(list, 72, 139, 206);
		Add(list, 72, 184);
		Imm64(list, (ulong)formatter);
		Add(list, 255, 208);
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 137, 66, 24);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static byte[] BuildProtoHttpLoopbackUrlShimShell(long data, long resume, byte[] displaced)
	{
		List<byte> list = new List<byte>();
		Add(list, 156);
		Add(list, 80);
		Add(list, 81);
		Add(list, 82);
		Add(list, 65, 82);
		Add(list, 65, 83);
		Add(list, 73, 186);
		Imm64(list, (ulong)data);
		Add(list, 73, 255, 2);
		Add(list, 73, 137, 82, 8);
		Add(list, 77, 137, 66, 40);
		Add(list, 77, 137, 74, 48);
		Add(list, 72, 133, 210);
		int displacementIndex = EmitRel32Placeholder(list, 15, 132);
		Add(list, 129, 58, 47, 97, 112, 105);
		int displacementIndex2 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 128, 122, 4, 47);
		int displacementIndex3 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 73, 255, 66, 16);
		Add(list, 233);
		int count = list.Count;
		Add(list, default(byte), default(byte), default(byte), default(byte));
		int count2 = list.Count;
		PatchRel32(list, displacementIndex2, count2);
		PatchRel32(list, displacementIndex3, count2);
		Add(list, 102, 129, 58, 47, 48);
		int displacementIndex4 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 128, 122, 2, 47);
		int displacementIndex5 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 73, 255, 66, 56);
		Add(list, 233);
		int count3 = list.Count;
		Add(list, default(byte), default(byte), default(byte), default(byte));
		int count4 = list.Count;
		PatchRel32(list, displacementIndex4, count4);
		PatchRel32(list, displacementIndex5, count4);
		Add(list, 102, 129, 58, 48, 47);
		int displacementIndex6 = EmitRel32Placeholder(list, 15, 133);
		Add(list, 73, 255, 66, 56);
		Add(list, 233);
		int count5 = list.Count;
		Add(list, default(byte), default(byte), default(byte), default(byte));
		int count6 = list.Count;
		PatchRel32(list, count, count6);
		PatchRel32(list, count3, count6);
		Add(list, 73, 137, 82, 24);
		Add(list, 72, 184);
		Imm64(list, (ulong)(data + 512));
		Add(list, 73, 187);
		Imm64(list, (ulong)(data + 1024));
		int count7 = list.Count;
		Add(list, 138, 8);
		Add(list, 132, 201);
		int displacementIndex7 = EmitRel8Placeholder(list, 116);
		Add(list, 65, 136, 11);
		Add(list, 72, 255, 192);
		Add(list, 73, 255, 195);
		int displacementIndex8 = EmitRel8Placeholder(list, 235);
		PatchRel8(list, displacementIndex8, count7);
		int count8 = list.Count;
		PatchRel8(list, displacementIndex7, count8);
		Add(list, 73, 139, 66, 24);
		int count9 = list.Count;
		Add(list, 138, 8);
		Add(list, 65, 136, 11);
		Add(list, 72, 255, 192);
		Add(list, 73, 255, 195);
		Add(list, 132, 201);
		int displacementIndex9 = EmitRel8Placeholder(list, 117);
		PatchRel8(list, displacementIndex9, count9);
		Add(list, 72, 184);
		Imm64(list, (ulong)(data + 1024));
		Add(list, 73, 137, 66, 32);
		Add(list, 72, 137, 68, 36, 16);
		int count10 = list.Count;
		PatchRel32(list, displacementIndex, count10);
		PatchRel32(list, displacementIndex6, count10);
		Add(list, 65, 91);
		Add(list, 65, 90);
		Add(list, 90);
		Add(list, 89);
		Add(list, 88);
		Add(list, 157);
		Add(list, displaced);
		EmitAbsJump(list, (ulong)resume);
		int count11 = list.Count;
		PatchRel32(list, count5, count11);
		Add(list, 73, 137, 82, 24);
		Add(list, 72, 184);
		Imm64(list, (ulong)(data + 512));
		Add(list, 73, 187);
		Imm64(list, (ulong)(data + 1024));
		int count12 = list.Count;
		Add(list, 138, 8);
		Add(list, 132, 201);
		int displacementIndex10 = EmitRel8Placeholder(list, 116);
		Add(list, 65, 136, 11);
		Add(list, 72, 255, 192);
		Add(list, 73, 255, 195);
		int displacementIndex11 = EmitRel8Placeholder(list, 235);
		PatchRel8(list, displacementIndex11, count12);
		int count13 = list.Count;
		PatchRel8(list, displacementIndex10, count13);
		Add(list, 65, 198, 3, 47);
		Add(list, 73, 255, 195);
		Add(list, 73, 139, 66, 24);
		int count14 = list.Count;
		Add(list, 138, 8);
		Add(list, 65, 136, 11);
		Add(list, 72, 255, 192);
		Add(list, 73, 255, 195);
		Add(list, 132, 201);
		int displacementIndex12 = EmitRel8Placeholder(list, 117);
		PatchRel8(list, displacementIndex12, count14);
		Add(list, 72, 184);
		Imm64(list, (ulong)(data + 1024));
		Add(list, 73, 137, 66, 32);
		Add(list, 72, 137, 68, 36, 16);
		Add(list, 65, 91);
		Add(list, 65, 90);
		Add(list, 90);
		Add(list, 89);
		Add(list, 88);
		Add(list, 157);
		Add(list, displaced);
		EmitAbsJump(list, (ulong)resume);
		return list.ToArray();
	}

	private static bool InstallHook(IntPtr handle, long moduleBase, HookSpec spec, bool mode2, out int error)
	{
		if (!ResolveHookTarget(handle, moduleBase, spec, out error))
		{
			return false;
		}
		long resolvedTarget = spec.ResolvedTarget;
		spec.Original = Read(handle, resolvedTarget, spec.PatchLen, out error);
		if (spec.Original.Length != spec.PatchLen)
		{
			return false;
		}
		IntPtr intPtr = VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)8192u, 12288u, 64u);
		if (intPtr == IntPtr.Zero)
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		spec.Remote = intPtr.ToInt64();
		spec.Data = spec.Remote + 4096;
		byte[] bytes;
		if (spec.Kind == "key")
		{
			bytes = BuildKeyShimShell(spec.Data, resolvedTarget + spec.PatchLen);
		}
		else if (spec.Kind == "lookup")
		{
			bytes = BuildLookupResultShell(spec.Data, resolvedTarget + spec.PatchLen, injectOwner: false);
		}
		else if (spec.Kind == "gate1")
		{
			bytes = BuildOwnerGate1ResultShell(spec.Data, moduleBase + 51239497, resolvedTarget + spec.PatchLen);
		}
		else if (spec.Kind == "gate2")
		{
			bytes = BuildOwnerGate2ResultShell(spec.Data, moduleBase + 51239497, resolvedTarget + spec.PatchLen);
		}
		else if (!(spec.Kind == "gate3"))
		{
			bytes = ((spec.Kind == "allocator") ? BuildAllocatorResultTraceShell(spec.Data, moduleBase + 51239438, resolvedTarget + spec.PatchLen) : ((spec.Kind == "category") ? BuildOwnerCategoryShimShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original, moduleBase + 186259584, moduleBase + 186259624) : ((spec.Kind == "dispatch") ? BuildCallbackCDispatchSourceShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original, moduleBase + 45046864) : ((spec.Kind == "httprecv") ? BuildMcrHttpRecvResultShell(spec.Data, moduleBase) : ((spec.Kind == "httpstatus") ? BuildMcrHttpStatusResultShell(spec.Data, moduleBase) : ((spec.Kind == "localurl") ? BuildLocalUrlShimShell(spec.Data, resolvedTarget + spec.PatchLen, moduleBase + 186259792, moduleBase + 51223664) : ((spec.Kind == "protohttplocalurl") ? BuildProtoHttpLoopbackUrlShimShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original) : ((spec.Kind == "httpmethod") ? BuildMcrHttpFailureMethodShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original) : ((!(spec.Kind == "httpfail")) ? BuildGenericTraceShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original) : BuildMcrHttpFailureDecisionShell(spec.Data, resolvedTarget + spec.PatchLen, spec.Original))))))))));
		}
		else
		{
			int num = BitConverter.ToInt32(spec.Original, 4);
			int num2 = BitConverter.ToInt32(spec.Original, 14);
			int num3 = BitConverter.ToInt32(spec.Original, 27);
			long num4 = resolvedTarget + 8 + num;
			long num5 = resolvedTarget + 18 + num2;
			long num6 = resolvedTarget + 31 + num3;
			if (num4 != num5 || num4 != num6)
			{
				error = 30224;
				return false;
			}
			bytes = BuildOwnerGate3ResultShell(spec.Data, num4, resolvedTarget + spec.PatchLen);
		}
		if (!Write(handle, spec.Remote, bytes, out error))
		{
			return false;
		}
		if (!Write(handle, spec.Data, new byte[1024], out error))
		{
			return false;
		}
		if (spec.Kind == "localurl" || spec.Kind == "protohttplocalurl")
		{
			string text = LocalLoopbackBaseUrl ?? "";
			byte[] bytes2 = Encoding.ASCII.GetBytes(text + "\0");
			if (bytes2.Length < 2 || bytes2.Length > 256)
			{
				error = 30209;
				return false;
			}
			if (!Write(handle, spec.Data + 512, bytes2, out error))
			{
				return false;
			}
		}
		List<byte> list = new List<byte>();
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)spec.Remote);
		while (list.Count < spec.PatchLen)
		{
			list.Add(144);
		}
		if (!VirtualProtectEx(handle, new IntPtr(resolvedTarget), (UIntPtr)(ulong)spec.PatchLen, 64u, out var oldProtect))
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		bool result = Write(handle, resolvedTarget, list.ToArray(), out error);
		FlushInstructionCache(handle, new IntPtr(resolvedTarget), (UIntPtr)(ulong)spec.PatchLen);
		VirtualProtectEx(handle, new IntPtr(resolvedTarget), (UIntPtr)(ulong)spec.PatchLen, oldProtect, out var _);
		return result;
	}

	private static ulong DataU64(IntPtr handle, HookSpec hook, int offset)
	{
		if (!ReadU64(handle, hook.Data + offset, out var value, out var _))
		{
			return 0uL;
		}
		return value;
	}

	private static void WriteDirectCallAnalysis(IntPtr handle, string path, long moduleBase, long returnRva, long expectedTargetRva, string label)
	{
		long num = moduleBase + returnRva;
		long address = num - 5;
		int error;
		byte[] array = Read(handle, address, 5, out error);
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Label: " + label);
		streamWriter.WriteLine("Return RVA: 0x" + returnRva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Candidate call RVA: 0x" + (returnRva - 5).ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Bytes: " + BytesHex(array));
		streamWriter.WriteLine("Read Win32: " + error.ToString(CultureInfo.InvariantCulture));
		if (array.Length == 5 && array[0] == 232)
		{
			int num2 = BitConverter.ToInt32(array, 1);
			long num3 = num + num2;
			streamWriter.WriteLine("Direct E8 call: YES");
			streamWriter.WriteLine("Decoded target VA: " + H(num3));
			streamWriter.WriteLine("Decoded target RVA: 0x" + (num3 - moduleBase).ToString("X", CultureInfo.InvariantCulture));
			streamWriter.WriteLine("Expected target RVA: 0x" + expectedTargetRva.ToString("X", CultureInfo.InvariantCulture));
			streamWriter.WriteLine("Target matches expected: " + (num3 - moduleBase == expectedTargetRva));
		}
		else
		{
			streamWriter.WriteLine("Direct E8 call: NO/UNKNOWN");
		}
	}

	private static void DumpTraceContext(IntPtr handle, string directory, string prefix, HookSpec hook, long moduleBase, int primaryObjectLength)
	{
		ulong num = DataU64(handle, hook, 16);
		ulong value = DataU64(handle, hook, 24);
		ulong num2 = DataU64(handle, hook, 32);
		ulong value2 = DataU64(handle, hook, 40);
		ulong num3 = DataU64(handle, hook, 48);
		ulong value3 = DataU64(handle, hook, 56);
		ulong num4 = DataU64(handle, hook, 64);
		ulong num5 = DataU64(handle, hook, 72);
		ulong value4 = DataU64(handle, hook, 80);
		ulong value5 = DataU64(handle, hook, 88);
		ulong num6 = DataU64(handle, hook, 96);
		ulong value6 = DataU64(handle, hook, 104);
		ulong num7 = DataU64(handle, hook, 112);
		ulong num8 = DataU64(handle, hook, 120);
		using (StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, prefix + "_registers.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("Calls: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("RAX: " + HU(DataU64(handle, hook, 8)));
			streamWriter.WriteLine("RCX: " + HU(num));
			streamWriter.WriteLine("RDX: " + HU(value));
			streamWriter.WriteLine("R8:  " + HU(num2));
			streamWriter.WriteLine("R9:  " + HU(value2));
			streamWriter.WriteLine("RBX: " + HU(num3));
			streamWriter.WriteLine("RBP: " + HU(value3));
			streamWriter.WriteLine("RSI: " + HU(num4));
			streamWriter.WriteLine("RDI: " + HU(num5));
			streamWriter.WriteLine("R12: " + HU(value4));
			streamWriter.WriteLine("R13: " + HU(value5));
			streamWriter.WriteLine("R14: " + HU(num6));
			streamWriter.WriteLine("R15: " + HU(value6));
			streamWriter.WriteLine("Original RSP: " + HU(num7));
			streamWriter.WriteLine("Immediate return: " + HU(num8));
			if (num8 >= (ulong)moduleBase)
			{
				streamWriter.WriteLine("Immediate return RVA: 0x" + (num8 - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture));
			}
		}
		DumpObject(handle, directory, prefix + "_RCX_object", num, primaryObjectLength);
		DumpObject(handle, directory, prefix + "_R8_object", num2, 2048);
		DumpObject(handle, directory, prefix + "_RBX_object", num3, 2048);
		DumpObject(handle, directory, prefix + "_RSI_object", num4, 2048);
		DumpObject(handle, directory, prefix + "_RDI_object", num5, 2048);
		DumpObject(handle, directory, prefix + "_R14_object", num6, 2048);
		if (num7 >= 4096 && Readable(handle, (long)num7))
		{
			long num9 = (long)(num7 - 1024);
			if (num9 > 0 && Readable(handle, num9))
			{
				Dump(handle, directory, prefix + "_stack_rsp_minus_0x400", num9, 4096);
			}
		}
		if (num8 >= (ulong)moduleBase && num8 < (ulong)(moduleBase + 536870912))
		{
			long address = (long)(num8 - 384);
			Dump(handle, directory, prefix + "_immediate_caller_code", address, 768);
		}
	}

	private static void DumpDispatchSourceContext(IntPtr handle, string directory, HookSpec hook, long moduleBase, int moduleSize)
	{
		ulong num = DataU64(handle, hook, 0);
		ulong value = DataU64(handle, hook, 8);
		ulong num2 = DataU64(handle, hook, 16);
		ulong num3 = DataU64(handle, hook, 24);
		ulong num4 = DataU64(handle, hook, 32);
		ulong value2 = DataU64(handle, hook, 40);
		ulong num5 = DataU64(handle, hook, 48);
		ulong num6 = DataU64(handle, hook, 56);
		ulong num7 = DataU64(handle, hook, 64);
		ulong num8 = DataU64(handle, hook, 72);
		ulong num9 = DataU64(handle, hook, 80);
		ulong num10 = DataU64(handle, hook, 88);
		ulong num11 = DataU64(handle, hook, 96);
		ulong num12 = DataU64(handle, hook, 104);
		ulong num13 = DataU64(handle, hook, 112);
		ulong num14 = DataU64(handle, hook, 120);
		ulong num15 = DataU64(handle, hook, 128);
		ulong num16 = DataU64(handle, hook, 136);
		ulong num17 = DataU64(handle, hook, 144);
		ulong num18 = DataU64(handle, hook, 152);
		ulong value3 = DataU64(handle, hook, 160);
		ulong value4 = DataU64(handle, hook, 168);
		ulong value5 = DataU64(handle, hook, 176);
		ulong value6 = DataU64(handle, hook, 184);
		using (StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "callback_C_dispatch_SOURCE_registers.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("Filtered Callback-C dispatch calls: " + num.ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("SOURCE RAX: " + HU(value));
			streamWriter.WriteLine("SOURCE RCX: " + HU(num2) + "  CL(success input)=" + ((byte)num2).ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("SOURCE RDX(payload input): " + HU(num3));
			streamWriter.WriteLine("SOURCE R8:  " + HU(num4) + "  R8B=" + ((byte)num4).ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("SOURCE R9:  " + HU(value2));
			streamWriter.WriteLine("SOURCE RBX: " + HU(num5));
			streamWriter.WriteLine("SOURCE RBP: " + HU(num6));
			streamWriter.WriteLine("SOURCE RSI: " + HU(num7));
			streamWriter.WriteLine("SOURCE RDI: " + HU(num8));
			streamWriter.WriteLine("SOURCE R12: " + HU(num9));
			streamWriter.WriteLine("SOURCE R13: " + HU(num10));
			streamWriter.WriteLine("SOURCE R14: " + HU(num11));
			streamWriter.WriteLine("SOURCE R15: " + HU(num12));
			streamWriter.WriteLine("Wrapper-entry RSP: " + HU(num13));
			streamWriter.WriteLine("UPSTREAM return VA: " + HU(num14));
			if (num14 >= (ulong)moduleBase && num14 < (ulong)(moduleBase + moduleSize))
			{
				streamWriter.WriteLine("UPSTREAM return RVA: 0x" + (num14 - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture));
			}
			streamWriter.WriteLine("Fifth-argument holder pointer: " + HU(num15));
			streamWriter.WriteLine("Callback descriptor: " + HU(num16));
			streamWriter.WriteLine("Resolved callback target: " + HU(num17));
			streamWriter.WriteLine("Resolved callback target RVA: " + ((num17 >= (ulong)moduleBase && num17 < (ulong)(moduleBase + moduleSize)) ? ("0x" + (num17 - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture)) : "<outside module>"));
			streamWriter.WriteLine("Callback context: " + HU(num18));
			streamWriter.WriteLine("Stack +0x30: " + HU(value3));
			streamWriter.WriteLine("Stack +0x38: " + HU(value4));
			streamWriter.WriteLine("Stack +0x40: " + HU(value5));
			streamWriter.WriteLine("Stack +0x48: " + HU(value6));
		}
		int error;
		byte[] array = Read(handle, hook.Data + 256, 512, out error);
		File.WriteAllBytes(Path.Combine(directory, "callback_C_dispatch_SOURCE_live_stack_0x200.bin"), array);
		WriteQwords(Path.Combine(directory, "callback_C_dispatch_SOURCE_live_stack_0x200_qwords.csv"), array, (long)num13);
		using (StreamWriter streamWriter2 = new StreamWriter(Path.Combine(directory, "callback_C_dispatch_SOURCE_live_stack_0x200.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter2.WriteLine("Original wrapper-entry stack base: " + HU(num13));
			streamWriter2.WriteLine("Captured immediately inside dispatch hook: 0x" + array.Length.ToString("X", CultureInfo.InvariantCulture));
			streamWriter2.WriteLine("Read Win32: " + error.ToString(CultureInfo.InvariantCulture));
			for (int i = 0; i < array.Length; i += 16)
			{
				int num19 = Math.Min(16, array.Length - i);
				StringBuilder stringBuilder = new StringBuilder();
				for (int j = 0; j < num19; j++)
				{
					stringBuilder.Append(array[i + j].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
				}
				streamWriter2.WriteLine(H((long)num13 + (long)i) + "  " + stringBuilder.ToString());
			}
		}
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_payload_RDX", num3, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_RBX_object", num5, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_RBP_object", num6, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_RSI_object", num7, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_RDI_object", num8, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_R12_object", num9, 2048);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_R13_object", num10, 2048);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_R14_object", num11, 4096);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_R15_object", num12, 2048);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_fifth_arg_holder", num15, 256);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_callback_descriptor", num16, 256);
		DumpObject(handle, directory, "callback_C_dispatch_SOURCE_callback_context", num18, 5120);
		if (num14 >= (ulong)moduleBase && num14 < (ulong)(moduleBase + moduleSize))
		{
			long returnRva = (long)num14 - moduleBase;
			long address = (long)(num14 - 768);
			Dump(handle, directory, "callback_C_dispatch_SOURCE_upstream_caller_code", address, 1792);
			WriteDirectCallAnalysis(handle, Path.Combine(directory, "callback_C_dispatch_SOURCE_upstream_call_analysis.txt"), moduleBase, returnRva, 45024720L, "Callback C dispatch wrapper upstream caller");
		}
	}

	private static void Dump(IntPtr handle, string directory, string name, long address, int length)
	{
		int error;
		byte[] array = Read(handle, address, length, out error);
		File.WriteAllBytes(Path.Combine(directory, name + ".bin"), array);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, name + ".txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Address: " + H(address));
		streamWriter.WriteLine("Requested: 0x" + length.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Captured: 0x" + array.Length.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Win32: " + error.ToString(CultureInfo.InvariantCulture));
		for (int i = 0; i < array.Length; i += 16)
		{
			int num = Math.Min(16, array.Length - i);
			StringBuilder stringBuilder = new StringBuilder();
			for (int j = 0; j < num; j++)
			{
				stringBuilder.Append(array[i + j].ToString("X2", CultureInfo.InvariantCulture)).Append(' ');
			}
			streamWriter.WriteLine(H(address + i) + "  " + stringBuilder.ToString());
		}
	}

	private static void WriteQwords(string path, byte[] bytes, long baseAddress)
	{
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("offset,address,value");
		for (int i = 0; i + 8 <= bytes.Length; i += 8)
		{
			ulong value = BitConverter.ToUInt64(bytes, i);
			streamWriter.WriteLine("0x" + i.ToString("X", CultureInfo.InvariantCulture) + "," + H(baseAddress + i) + "," + HU(value));
		}
	}

	private static void DumpObject(IntPtr handle, string directory, string label, ulong address, int length)
	{
		if (address < 65536 || !Readable(handle, (long)address))
		{
			return;
		}
		int error;
		byte[] array = Read(handle, (long)address, length, out error);
		File.WriteAllBytes(Path.Combine(directory, label + ".bin"), array);
		WriteQwords(Path.Combine(directory, label + "_qwords.csv"), array, (long)address);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, label + "_ascii.csv"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("offset,length,text");
		int i = 0;
		while (i < array.Length)
		{
			if (array[i] >= 32 && array[i] <= 126)
			{
				int num = i;
				for (; i < array.Length && array[i] >= 32 && array[i] <= 126; i++)
				{
				}
				if (i - num >= 4)
				{
					string text = Encoding.ASCII.GetString(array, num, i - num).Replace("\"", "\"\"");
					streamWriter.WriteLine("0x" + num.ToString("X", CultureInfo.InvariantCulture) + "," + (i - num).ToString(CultureInfo.InvariantCulture) + ",\"" + text + "\"");
				}
			}
			else
			{
				i++;
			}
		}
	}

	private static void WriteGenericTraceSummary(IntPtr handle, string directory, HookSpec hook, long moduleBase, int moduleSize)
	{
		int error;
		byte[] bytes = Read(handle, hook.Data, 256, out error);
		File.WriteAllBytes(Path.Combine(directory, hook.Name + "_data.bin"), bytes);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, hook.Name + "_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Nominal RVA: 0x" + hook.Rva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Resolved RVA: 0x" + hook.ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Validation: " + hook.ValidationNote);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("RAX: " + HU(DataU64(handle, hook, 8)));
		streamWriter.WriteLine("RCX: " + HU(DataU64(handle, hook, 16)));
		streamWriter.WriteLine("RDX: " + HU(DataU64(handle, hook, 24)));
		streamWriter.WriteLine("R8:  " + HU(DataU64(handle, hook, 32)));
		streamWriter.WriteLine("R9:  " + HU(DataU64(handle, hook, 40)));
		streamWriter.WriteLine("RBX: " + HU(DataU64(handle, hook, 48)));
		streamWriter.WriteLine("RBP: " + HU(DataU64(handle, hook, 56)));
		streamWriter.WriteLine("RSI: " + HU(DataU64(handle, hook, 64)));
		streamWriter.WriteLine("RDI: " + HU(DataU64(handle, hook, 72)));
		streamWriter.WriteLine("R12: " + HU(DataU64(handle, hook, 80)));
		streamWriter.WriteLine("R13: " + HU(DataU64(handle, hook, 88)));
		streamWriter.WriteLine("R14: " + HU(DataU64(handle, hook, 96)));
		streamWriter.WriteLine("R15: " + HU(DataU64(handle, hook, 104)));
		ulong value = DataU64(handle, hook, 112);
		ulong num = DataU64(handle, hook, 120);
		streamWriter.WriteLine("Original RSP: " + HU(value));
		streamWriter.WriteLine("Immediate return: " + HU(num));
		if (num >= (ulong)moduleBase && num < (ulong)(moduleBase + moduleSize))
		{
			streamWriter.WriteLine("Immediate return RVA: 0x" + (num - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture));
		}
	}

	private static void WriteMcrHttpRecvResultSummary(IntPtr handle, string directory, HookSpec hook)
	{
		string path = Path.Combine(directory, "MCR_HTTP_RECV_RESULT_RVA_30DD275_summary.txt");
		ulong num = DataU64(handle, hook, 8);
		int num2 = (int)num;
		string text = ((num2 == -3) ? "PENDING_NOT_FINAL" : ((num2 < 0) ? "LOW_LEVEL_FAILURE" : "NONNEGATIVE_STATUS_AVAILABLE"));
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last receive/result EAX signed low32: " + num2.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last receive/result raw: " + HU(num));
		streamWriter.WriteLine("Classification: " + text);
		streamWriter.WriteLine("Request RBX: " + HU(DataU64(handle, hook, 16)));
		streamWriter.WriteLine("Request state [RBX+0x260]: " + DataU64(handle, hook, 24).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Low-level request handle [RBX+0x20]: " + HU(DataU64(handle, hook, 32)));
		streamWriter.WriteLine("Original RSP: " + HU(DataU64(handle, hook, 40)));
		streamWriter.WriteLine();
		streamWriter.WriteLine("Stock branch meaning: -3 waits/returns without failure; any other negative result goes to the request failure method; nonnegative proceeds to query status tag 0x636F6465 ('code').");
	}

	private static void WriteMcrHttpStatusResultSummary(IntPtr handle, string directory, HookSpec hook)
	{
		string path = Path.Combine(directory, "MCR_HTTP_STATUS_RESULT_RVA_30DD29C_summary.txt");
		uint num = (uint)DataU64(handle, hook, 8);
		string text = ((num >= 200 && num < 300) ? "HTTP_2XX_SUCCESS_RANGE" : ((num == 408) ? "HTTP_408_RETRY_PATH" : ((num >= 200) ? "HTTP_NON_2XX_FAILURE_RANGE" : "STATUS_BELOW_200_FAILURE_RANGE")));
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last status/code EAX: " + num.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Classification: " + text);
		streamWriter.WriteLine("Request RBX: " + HU(DataU64(handle, hook, 16)));
		streamWriter.WriteLine("Prior receive/result RDI signed low32: " + ((int)DataU64(handle, hook, 24)).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("408 retry count before status branch [RBX+0x37C]: " + DataU64(handle, hook, 32).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Request state [RBX+0x260]: " + DataU64(handle, hook, 40).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Original RSP: " + HU(DataU64(handle, hook, 48)));
		streamWriter.WriteLine();
		streamWriter.WriteLine("Stock branch meaning: 200-299 invokes vtable +0x20 success; status 408 may retry up to five times; other non-2xx values ultimately invoke vtable +0x28 failure.");
	}

	private static void WriteMcrHttpFailureMethodSummary(IntPtr handle, string directory, HookSpec hook, long moduleBase, int moduleSize)
	{
		string path = Path.Combine(directory, "MCR_HTTP_FAILURE_METHOD_RVA_30DC230_summary.txt");
		ulong num = DataU64(handle, hook, 16);
		ulong num2 = DataU64(handle, hook, 88);
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Request object RCX: " + HU(DataU64(handle, hook, 8)));
		streamWriter.WriteLine("Request vtable: " + HU(num));
		if (num >= (ulong)moduleBase && num < (ulong)(moduleBase + moduleSize))
		{
			streamWriter.WriteLine("Request vtable RVA: 0x" + (num - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture));
		}
		streamWriter.WriteLine("Pre-failure request state [RCX+0x260]: " + DataU64(handle, hook, 24).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("408 retry count [RCX+0x37C]: " + DataU64(handle, hook, 32).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Low-level request handle [RCX+0x20]: " + HU(DataU64(handle, hook, 40)));
		streamWriter.WriteLine("Response buffer [RCX+0x398]: " + HU(DataU64(handle, hook, 48)));
		streamWriter.WriteLine("Field +0x3A4: " + DataU64(handle, hook, 56).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Field +0x3A8: " + DataU64(handle, hook, 64).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Field +0x408: " + DataU64(handle, hook, 72).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Original RSP: " + HU(DataU64(handle, hook, 80)));
		streamWriter.WriteLine("Immediate return: " + HU(num2));
		if (num2 >= (ulong)moduleBase && num2 < (ulong)(moduleBase + moduleSize))
		{
			streamWriter.WriteLine("Immediate return RVA: 0x" + (num2 - (ulong)moduleBase).ToString("X", CultureInfo.InvariantCulture));
		}
	}

	private static void WriteReadinessSummary(IntPtr handle, string directory, HookSpec hook)
	{
		int error;
		byte[] bytes = Read(handle, hook.Data, 128, out error);
		File.WriteAllBytes(Path.Combine(directory, hook.Name + "_data.bin"), bytes);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, hook.Name + "_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Original readiness byte [context+0x08]: " + DataU64(handle, hook, 8).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Next context pointer [context+0x10]: " + HU(DataU64(handle, hook, 16)));
		streamWriter.WriteLine("Helper context RDI: " + HU(DataU64(handle, hook, 24)));
		streamWriter.WriteLine("Zero-readiness bypass injections: " + DataU64(handle, hook, 32).ToString(CultureInfo.InvariantCulture));
	}

	private static void WriteAllocatorSummary(IntPtr handle, string directory, HookSpec hook)
	{
		int error;
		byte[] bytes = Read(handle, hook.Data, 128, out error);
		File.WriteAllBytes(Path.Combine(directory, hook.Name + "_data.bin"), bytes);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, hook.Name + "_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Allocator return RAX: " + HU(DataU64(handle, hook, 8)));
		streamWriter.WriteLine("Current owner context RDI: " + HU(DataU64(handle, hook, 16)));
	}

	private static void DumpRawData(IntPtr handle, string directory, HookSpec hook)
	{
		int error;
		byte[] bytes = Read(handle, hook.Data, 256, out error);
		File.WriteAllBytes(Path.Combine(directory, hook.Name + "_data.bin"), bytes);
	}

	private static void WriteGateResultSummary(IntPtr handle, string directory, HookSpec hook, string label)
	{
		DumpRawData(handle, directory, hook);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, hook.Name + "_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Nominal RVA: 0x" + hook.Rva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Resolved RVA: 0x" + hook.ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Validation: " + hook.ValidationNote);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine(label + ": " + HU(DataU64(handle, hook, 8)));
	}

	private static void WriteGate3Summary(IntPtr handle, string directory, HookSpec hook)
	{
		DumpRawData(handle, directory, hook);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, hook.Name + "_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("Nominal RVA: 0x" + hook.Rva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Resolved RVA: 0x" + hook.ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Validation: " + hook.ValidationNote);
		streamWriter.WriteLine("Invocation count: " + DataU64(handle, hook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Original 0x30A5740 result EAX: " + DataU64(handle, hook, 8).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Original readiness byte [helper context+0x08]: " + DataU64(handle, hook, 16).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Next context pointer [helper context+0x10]: " + HU(DataU64(handle, hook, 24)));
		streamWriter.WriteLine("Original helper-context RDI: " + HU(DataU64(handle, hook, 32)));
		streamWriter.WriteLine("Service-probe local bypasses: " + DataU64(handle, hook, 40).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Readiness local bypasses: " + DataU64(handle, hook, 48).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Post-bypass failure code: " + DataU64(handle, hook, 56).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("  3 = next context pointer was zero");
		streamWriter.WriteLine("  4 = next context allocator/service interface was zero");
		streamWriter.WriteLine("  0 = both locally bypassed prerequisites reached a usable next context");
		streamWriter.WriteLine("Next context allocator/service interface [next+0x08]: " + HU(DataU64(handle, hook, 64)));
	}

	private static void WriteSpecialSummaries(IntPtr handle, string directory, HookSpec keyHook, HookSpec lookupHook)
	{
		int error;
		byte[] bytes = Read(handle, keyHook.Data, 128, out error);
		File.WriteAllBytes(Path.Combine(directory, "key_shim_data.bin"), bytes);
		byte[] bytes2 = Read(handle, lookupHook.Data, 128, out error);
		File.WriteAllBytes(Path.Combine(directory, "lookup_result_data.bin"), bytes2);
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "identity_shim_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("KEY GATE");
		streamWriter.WriteLine("calls=" + DataU64(handle, keyHook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("original_blaze_key=" + HU(DataU64(handle, keyHook, 8)));
		streamWriter.WriteLine("effective_blaze_key=" + HU(DataU64(handle, keyHook, 16)));
		streamWriter.WriteLine("synthetic_key_injections=" + DataU64(handle, keyHook, 24).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("parent_RDI=" + HU(DataU64(handle, keyHook, 32)));
		streamWriter.WriteLine("prelookup_RBP=" + HU(DataU64(handle, keyHook, 40)));
		streamWriter.WriteLine();
		streamWriter.WriteLine("USERMANAGER LOOKUP RESULT");
		streamWriter.WriteLine("calls=" + DataU64(handle, lookupHook, 0).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("returned_User_ptr=" + HU(DataU64(handle, lookupHook, 8)));
		streamWriter.WriteLine("AccountId_before_surrogate=" + HU(DataU64(handle, lookupHook, 16)));
		streamWriter.WriteLine("effective_AccountId=" + HU(DataU64(handle, lookupHook, 24)));
		streamWriter.WriteLine("synthetic_owner_injections=" + DataU64(handle, lookupHook, 32).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("parent_RDI=" + HU(DataU64(handle, lookupHook, 40)));
	}

	private static void DumpBatchCandidateTargets(IntPtr handle, string directory, string stem, ulong candidate, long moduleBase, int moduleSize)
	{
		if (candidate < 65536 || !Readable(handle, (long)candidate))
		{
			return;
		}
		DumpImmediateObjectSnapshot(handle, directory, stem + "_object_0x2000", candidate, 8192);
		int error;
		byte[] array = Read(handle, (long)candidate, 768, out error);
		HashSet<ulong> hashSet = new HashSet<ulong>();
		int num = 0;
		for (int i = 0; i + 8 <= array.Length; i += 8)
		{
			if (num >= 32)
			{
				break;
			}
			ulong num2 = BitConverter.ToUInt64(array, i);
			if (num2 >= 65536 && !hashSet.Contains(num2) && (num2 < (ulong)moduleBase || num2 >= (ulong)(moduleBase + moduleSize)) && Readable(handle, (long)num2))
			{
				hashSet.Add(num2);
				DumpImmediateObjectSnapshot(handle, directory, stem + "_ptr_" + i.ToString("X3", CultureInfo.InvariantCulture) + "_0x600", num2, 1536);
				num++;
			}
		}
	}

	private static BatchQueueWatchSession StartBatchQueueWatch(IntPtr handle, string outputDirectory, long ultimateCareer, long moduleBase, int moduleSize, HookSpec localUrlHook, HookSpec stage1SuccessHook, HookSpec stage2SuccessHook)
	{
		BatchQueueWatchSession session = new BatchQueueWatchSession();
		session.RootDirectory = Path.Combine(outputDirectory, "batch-queue-watch");
		Directory.CreateDirectory(session.RootDirectory);
		session.Worker = new Thread((ThreadStart)delegate
		{
			try
			{
				using StreamWriter streamWriter = new StreamWriter(Path.Combine(session.RootDirectory, "batch-queue-timeline.csv"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.WriteLine("elapsed_ms,event,count,slot1,slot2,slot3,protohttp_requests,stage1_success,stage2_success,detail");
				Stopwatch stopwatch = Stopwatch.StartNew();
				uint num = uint.MaxValue;
				ulong[] array = new ulong[3];
				while (!session.StopRequested)
				{
					if (!ReadU32(handle, ultimateCareer + 2008, out var value, out var error))
					{
						value = uint.MaxValue;
					}
					ulong[] array2 = new ulong[3];
					ReadU64(handle, ultimateCareer + 1976, out array2[0], out error);
					ReadU64(handle, ultimateCareer + 1984, out array2[1], out error);
					ReadU64(handle, ultimateCareer + 1992, out array2[2], out error);
					if (value != num || array2[0] != array[0] || array2[1] != array[1] || array2[2] != array[2])
					{
						long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",BATCH_FIELDS_CHANGED," + value.ToString(CultureInfo.InvariantCulture) + "," + HU(array2[0]) + "," + HU(array2[1]) + "," + HU(array2[2]) + "," + DataU64(handle, localUrlHook, 0).ToString(CultureInfo.InvariantCulture) + "," + DataU64(handle, stage1SuccessHook, 0).ToString(CultureInfo.InvariantCulture) + "," + DataU64(handle, stage2SuccessHook, 0).ToString(CultureInfo.InvariantCulture) + ",UltimateCareer+0x7B8/+0x7C0/+0x7C8 count+0x7D8");
						streamWriter.Flush();
						for (int i = 0; i < 3; i++)
						{
							if (array2[i] != 0L && array2[i] != array[i])
							{
								switch (i)
								{
								case 0:
									session.Slot1Generations++;
									break;
								case 1:
									session.Slot2Generations++;
									break;
								default:
									session.Slot3Generations++;
									break;
								}
								string rootDirectory = session.RootDirectory;
								string text = (i + 1).ToString(CultureInfo.InvariantCulture);
								string text2 = Path.Combine(rootDirectory, "slot" + text + "_generation_" + (i switch
								{
									1 => session.Slot2Generations, 
									0 => session.Slot1Generations, 
									_ => session.Slot3Generations, 
								}).ToString("D2", CultureInfo.InvariantCulture));
								Directory.CreateDirectory(text2);
								DumpBatchCandidateTargets(handle, text2, "slot" + (i + 1).ToString(CultureInfo.InvariantCulture) + "_FIRST", array2[i], moduleBase, moduleSize);
							}
						}
					}
					num = value;
					array[0] = array2[0];
					array[1] = array2[1];
					array[2] = array2[2];
					session.LastCount = value;
					session.Slot1 = array2[0];
					session.Slot2 = array2[1];
					session.Slot3 = array2[2];
					Thread.Sleep(5);
				}
			}
			catch (Exception ex)
			{
				try
				{
					File.WriteAllText(Path.Combine(session.RootDirectory, "batch-queue-watch-error.txt"), ex.ToString());
				}
				catch
				{
				}
			}
		});
		session.Worker.IsBackground = true;
		session.Worker.Start();
		return session;
	}

	private static void StopBatchQueueWatch(BatchQueueWatchSession session)
	{
		if (session == null)
		{
			return;
		}
		session.StopRequested = true;
		try
		{
			if (session.Worker != null && session.Worker.IsAlive)
			{
				session.Worker.Join(3000);
			}
		}
		catch
		{
		}
	}

	private static void WriteBatchQueueSummary(IntPtr handle, string directory, BatchQueueWatchSession session, long ultimateCareer, long moduleBase, int moduleSize)
	{
		string text = Path.Combine(directory, "batch-queue-final");
		Directory.CreateDirectory(text);
		ReadU32(handle, ultimateCareer + 2008, out var value, out var error);
		ReadU64(handle, ultimateCareer + 1976, out var value2, out error);
		ReadU64(handle, ultimateCareer + 1984, out var value3, out error);
		ReadU64(handle, ultimateCareer + 1992, out var value4, out error);
		using (StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "BATCH_QUEUE_PRE_MCR_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("Observed batch count [UltimateCareer+0x7D8]: " + value.ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("Candidate slot 1 [UltimateCareer+0x7B8]: " + HU(value2));
			streamWriter.WriteLine("Candidate slot 2 [UltimateCareer+0x7C0]: " + HU(value3));
			streamWriter.WriteLine("Candidate slot 3 [UltimateCareer+0x7C8]: " + HU(value4));
			streamWriter.WriteLine("Slot generations observed: " + session.Slot1Generations.ToString(CultureInfo.InvariantCulture) + ", " + session.Slot2Generations.ToString(CultureInfo.InvariantCulture) + ", " + session.Slot3Generations.ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("These offsets are CANDIDATE batch-entry fields based on v0.85: count became 3 while three dynamic pointers appeared immediately before it. v0.86 is intended to prove or reject that interpretation.");
		}
		DumpBatchCandidateTargets(handle, text, "slot1_FINAL", value2, moduleBase, moduleSize);
		DumpBatchCandidateTargets(handle, text, "slot2_FINAL", value3, moduleBase, moduleSize);
		DumpBatchCandidateTargets(handle, text, "slot3_FINAL", value4, moduleBase, moduleSize);
	}

	private static void DumpCurrentItemTargets(IntPtr handle, string directory, string stem, ulong transient, long moduleBase, int moduleSize)
	{
		if (transient < 65536 || !Readable(handle, (long)transient))
		{
			return;
		}
		DumpImmediateObjectSnapshot(handle, directory, stem + "_transient_0x1340", transient, 4928);
		int[] array = new int[14]
		{
			112, 120, 128, 152, 248, 256, 264, 344, 352, 360,
			432, 440, 448, 496
		};
		HashSet<ulong> hashSet = new HashSet<ulong>();
		for (int i = 0; i < array.Length; i++)
		{
			if (!ReadU64(handle, (long)transient + (long)array[i], out var value, out var error) || value < 65536 || hashSet.Contains(value) || !Readable(handle, (long)value))
			{
				continue;
			}
			hashSet.Add(value);
			string text = stem + "_ptr_" + array[i].ToString("X3", CultureInfo.InvariantCulture);
			DumpImmediateObjectSnapshot(handle, directory, text + "_0x1800", value, 6144);
			byte[] array2 = Read(handle, (long)value, 512, out error);
			int num = 0;
			HashSet<ulong> hashSet2 = new HashSet<ulong>();
			for (int j = 0; j + 8 <= array2.Length; j += 8)
			{
				if (num >= 16)
				{
					break;
				}
				ulong num2 = BitConverter.ToUInt64(array2, j);
				if (num2 >= 65536 && !hashSet2.Contains(num2) && (num2 < (ulong)moduleBase || num2 >= (ulong)(moduleBase + moduleSize)) && Readable(handle, (long)num2))
				{
					hashSet2.Add(num2);
					DumpImmediateObjectSnapshot(handle, directory, text + "_child_" + j.ToString("X3", CultureInfo.InvariantCulture) + "_0x800", num2, 2048);
					num++;
				}
			}
		}
	}

	private static CurrentItemWatchSession StartCurrentItemWatch(IntPtr handle, string outputDirectory, long manager, long moduleBase, int moduleSize, HookSpec callbackBHook, HookSpec localUrlHook, HookSpec stage1SuccessHook, HookSpec stage2SuccessHook)
	{
		CurrentItemWatchSession session = new CurrentItemWatchSession();
		session.RootDirectory = Path.Combine(outputDirectory, "current-item-pre-mcr-watch");
		Directory.CreateDirectory(session.RootDirectory);
		session.Worker = new Thread((ThreadStart)delegate
		{
			try
			{
				using StreamWriter streamWriter = new StreamWriter(Path.Combine(session.RootDirectory, "current-item-timeline.csv"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.WriteLine("elapsed_ms,event,transient,tb_id,remaining,total_work,completed_work,active,p70,p78,p80,protohttp_requests,callback_b_calls,stage1_success,stage2_success,detail");
				Stopwatch stopwatch = Stopwatch.StartNew();
				ulong num = 0uL;
				uint num2 = uint.MaxValue;
				uint num3 = uint.MaxValue;
				uint num4 = uint.MaxValue;
				uint num5 = uint.MaxValue;
				uint num6 = uint.MaxValue;
				ulong num7 = 0uL;
				ulong num8 = 0uL;
				ulong num9 = 0uL;
				ulong num10 = 0uL;
				long num11 = -1L;
				ulong num12 = 0uL;
				bool flag = false;
				while (!session.StopRequested)
				{
					long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
					if (!ReadU64(handle, manager + 432, out var value, out var error))
					{
						value = 0uL;
					}
					uint value2 = 0u;
					uint value3 = 0u;
					uint value4 = 0u;
					uint value5 = 0u;
					uint value6 = 0u;
					ulong value7 = 0uL;
					ulong value8 = 0uL;
					ulong value9 = 0uL;
					if (value >= 65536 && Readable(handle, (long)value))
					{
						ReadU32(handle, (long)(value + 108), out value2, out error);
						ReadU32(handle, (long)(value + 144), out value3, out error);
						ReadU32(handle, (long)(value + 160), out value4, out error);
						ReadU32(handle, (long)(value + 164), out value5, out error);
						ReadU32(handle, (long)(value + 168), out value6, out error);
						ReadU64(handle, (long)(value + 112), out value7, out error);
						ReadU64(handle, (long)(value + 120), out value8, out error);
						ReadU64(handle, (long)(value + 128), out value9, out error);
					}
					ulong num13 = DataU64(handle, localUrlHook, 0);
					ulong num14 = DataU64(handle, callbackBHook, 0);
					ulong num15 = DataU64(handle, stage1SuccessHook, 0);
					ulong num16 = DataU64(handle, stage2SuccessHook, 0);
					bool flag2 = value != num;
					bool flag3 = value2 != num2 && value2 != 0 && value2 != uint.MaxValue;
					if (flag2 || value2 != num2 || value3 != num3 || value4 != num4 || value5 != num5 || value6 != num6 || value7 != num7 || value8 != num8 || value9 != num9)
					{
						string text = (flag3 ? "CURRENT_ITEM_CHANGED" : (flag2 ? "TRANSIENT_CHANGED" : "CURRENT_ITEM_STATE_CHANGED"));
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "," + text + "," + HU(value) + "," + value2.ToString(CultureInfo.InvariantCulture) + "," + value3.ToString(CultureInfo.InvariantCulture) + "," + value4.ToString(CultureInfo.InvariantCulture) + "," + value5.ToString(CultureInfo.InvariantCulture) + "," + value6.ToString(CultureInfo.InvariantCulture) + "," + HU(value7) + "," + HU(value8) + "," + HU(value9) + "," + num13.ToString(CultureInfo.InvariantCulture) + "," + num14.ToString(CultureInfo.InvariantCulture) + "," + num15.ToString(CultureInfo.InvariantCulture) + "," + num16.ToString(CultureInfo.InvariantCulture) + ",manager+0x1B0 current-id+0x6C remaining+0x90 total+0xA0 completed+0xA4 active+0xA8");
						streamWriter.Flush();
					}
					if (flag3 && value >= 65536)
					{
						session.ItemSwitches++;
						num11 = elapsedMilliseconds;
						num12 = num13;
						flag = false;
						string text2 = Path.Combine(session.RootDirectory, "item_" + session.ItemSwitches.ToString("D2", CultureInfo.InvariantCulture) + "_tb_" + value2.ToString(CultureInfo.InvariantCulture) + "_switch");
						Directory.CreateDirectory(text2);
						DumpCurrentItemTargets(handle, text2, "SWITCH", value, moduleBase, moduleSize);
					}
					if (num14 != num10 && num14 != 0L)
					{
						session.CallbackBSnapshots++;
						string text3 = Path.Combine(session.RootDirectory, "callback_B_observed_" + num14.ToString("D2", CultureInfo.InvariantCulture));
						Directory.CreateDirectory(text3);
						DumpRawData(handle, text3, callbackBHook);
						DumpTraceContext(handle, text3, "callback_B_context", callbackBHook, moduleBase, 5120);
						if (value >= 65536)
						{
							DumpCurrentItemTargets(handle, text3, "AT_CALLBACK_B", value, moduleBase, moduleSize);
						}
						using StreamWriter streamWriter2 = new StreamWriter(Path.Combine(text3, "callback_B_correlated_state.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
						streamWriter2.WriteLine("elapsed_ms=" + elapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("callback_B_calls=" + num14.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("current_tb_id=" + value2.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("remaining=" + value3.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("total_work=" + value4.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("completed_work=" + value5.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("protohttp_requests=" + num13.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("stage1_success=" + num15.ToString(CultureInfo.InvariantCulture));
						streamWriter2.WriteLine("stage2_success=" + num16.ToString(CultureInfo.InvariantCulture));
					}
					if (!flag && num11 >= 0 && value2 != 0 && value3 != 0 && value5 < value4 && num13 == num12 && elapsedMilliseconds - num11 >= 1500)
					{
						flag = true;
						session.PreMcrStallDetected = true;
						session.StallTeamBuilderId = value2;
						session.StallElapsedMs = elapsedMilliseconds;
						string text4 = Path.Combine(session.RootDirectory, "AUTO_PRE_MCR_STALL_tb_" + value2.ToString(CultureInfo.InvariantCulture));
						Directory.CreateDirectory(text4);
						DumpCurrentItemTargets(handle, text4, "STALL_1500MS", value, moduleBase, moduleSize);
						Dump(handle, text4, "RVA_2AF5980_callback_B_code_0x800", moduleBase + 45046144, 2048);
						using (StreamWriter streamWriter3 = new StreamWriter(Path.Combine(text4, "stall-state.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
						{
							streamWriter3.WriteLine("elapsed_ms=" + elapsedMilliseconds.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("current_tb_id=" + value2.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("remaining=" + value3.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("total_work=" + value4.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("completed_work=" + value5.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("active=" + value6.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("protohttp_requests_at_switch=" + num12.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("protohttp_requests_now=" + num13.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("callback_B_calls=" + num14.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("stage1_success=" + num15.ToString(CultureInfo.InvariantCulture));
							streamWriter3.WriteLine("stage2_success=" + num16.ToString(CultureInfo.InvariantCulture));
						}
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",AUTO_PRE_MCR_STALL_SNAPSHOT," + HU(value) + "," + value2.ToString(CultureInfo.InvariantCulture) + "," + value3.ToString(CultureInfo.InvariantCulture) + "," + value4.ToString(CultureInfo.InvariantCulture) + "," + value5.ToString(CultureInfo.InvariantCulture) + "," + value6.ToString(CultureInfo.InvariantCulture) + "," + HU(value7) + "," + HU(value8) + "," + HU(value9) + "," + num13.ToString(CultureInfo.InvariantCulture) + "," + num14.ToString(CultureInfo.InvariantCulture) + "," + num15.ToString(CultureInfo.InvariantCulture) + "," + num16.ToString(CultureInfo.InvariantCulture) + ",no new MCR request for 1500ms after current-item switch");
						streamWriter.Flush();
					}
					num = value;
					num2 = value2;
					num3 = value3;
					num4 = value4;
					num5 = value5;
					num6 = value6;
					num7 = value7;
					num8 = value8;
					num9 = value9;
					num10 = num14;
					session.TransientPointer = value;
					session.CurrentTeamBuilderId = value2;
					session.RemainingItems = value3;
					session.TotalWorkUnits = value4;
					session.CompletedWorkUnits = value5;
					session.ActiveState = value6;
					session.CurrentPointer70 = value7;
					session.CurrentPointer78 = value8;
					session.CurrentPointer80 = value9;
					Thread.Sleep(5);
				}
			}
			catch (Exception ex)
			{
				try
				{
					File.WriteAllText(Path.Combine(session.RootDirectory, "current-item-watch-error.txt"), ex.ToString());
				}
				catch
				{
				}
			}
		});
		session.Worker.IsBackground = true;
		session.Worker.Start();
		return session;
	}

	private static void StopCurrentItemWatch(CurrentItemWatchSession session)
	{
		if (session == null)
		{
			return;
		}
		session.StopRequested = true;
		try
		{
			if (session.Worker != null && session.Worker.IsAlive)
			{
				session.Worker.Join(5000);
			}
		}
		catch
		{
		}
	}

	private static void WriteCurrentItemSummary(string directory, CurrentItemWatchSession session)
	{
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "CURRENT_ITEM_PRE_MCR_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Transient pointer: " + HU(session.TransientPointer));
		streamWriter.WriteLine("Current TeamBuilder ID [transient+0x6C]: " + session.CurrentTeamBuilderId.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Remaining selected items [transient+0x90]: " + session.RemainingItems.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Total work units [transient+0xA0]: " + session.TotalWorkUnits.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Completed work units [transient+0xA4]: " + session.CompletedWorkUnits.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Active/state [transient+0xA8]: " + session.ActiveState.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Current pointer +0x70: " + HU(session.CurrentPointer70));
		streamWriter.WriteLine("Current pointer +0x78: " + HU(session.CurrentPointer78));
		streamWriter.WriteLine("Current pointer +0x80: " + HU(session.CurrentPointer80));
		streamWriter.WriteLine("Current-item switches observed: " + session.ItemSwitches.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Callback-B snapshots observed: " + session.CallbackBSnapshots.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Automatic pre-MCR stall detected: " + session.PreMcrStallDetected);
		if (session.PreMcrStallDetected)
		{
			streamWriter.WriteLine("Stall TeamBuilder ID: " + session.StallTeamBuilderId.ToString(CultureInfo.InvariantCulture));
			streamWriter.WriteLine("Stall snapshot elapsed ms: " + session.StallElapsedMs.ToString(CultureInfo.InvariantCulture));
		}
		streamWriter.WriteLine();
		streamWriter.WriteLine("v0.86 established the reproduced three-item progression: TeamBuilder ID 832438297 -> 832438386 -> 832438392; remaining 3 -> 2 -> 1; total work=12; completed work 0 -> 4 -> 8. The visible stall is therefore exactly 8/12 (~2/3), with the third item selected but not reaching MCR.");
	}

	private static string SafeHexBytes(byte[] bytes, int offset, int count)
	{
		if (bytes == null || offset < 0 || offset >= bytes.Length || count <= 0)
		{
			return "";
		}
		int num = Math.Min(count, bytes.Length - offset);
		StringBuilder stringBuilder = new StringBuilder(num * 2);
		for (int i = 0; i < num; i++)
		{
			stringBuilder.Append(bytes[offset + i].ToString("X2", CultureInfo.InvariantCulture));
		}
		return stringBuilder.ToString();
	}

	private static void WriteDiffRows(StreamWriter writer, long elapsedMs, string region, long baseAddress, byte[] before, byte[] after)
	{
		if (writer == null || before == null || after == null)
		{
			return;
		}
		int num = Math.Min(before.Length, after.Length);
		int i = 0;
		while (i < num)
		{
			if (before[i] == after[i])
			{
				i++;
				continue;
			}
			int num2;
			for (num2 = i; i < num && before[i] != after[i] && i - num2 < 32; i++)
			{
			}
			int count = i - num2;
			writer.WriteLine(elapsedMs.ToString(CultureInfo.InvariantCulture) + "," + region + ",0x" + num2.ToString("X", CultureInfo.InvariantCulture) + "," + H(baseAddress + num2) + "," + count.ToString(CultureInfo.InvariantCulture) + "," + SafeHexBytes(before, num2, count) + "," + SafeHexBytes(after, num2, count));
		}
		writer.Flush();
	}

	private static void DumpImmediateObjectSnapshot(IntPtr handle, string directory, string stem, ulong address, int length)
	{
		try
		{
			if (address >= 65536 && Readable(handle, (long)address))
			{
				DumpObject(handle, directory, stem, address, length);
			}
		}
		catch
		{
		}
	}

	private static LifecycleWatchSession StartLifecycleWatch(IntPtr handle, string outputDirectory, long manager, HookSpec ctorHook, long moduleBase, int moduleSize)
	{
		LifecycleWatchSession session = new LifecycleWatchSession();
		session.StopRequested = false;
		session.RootDirectory = Path.Combine(outputDirectory, "post-owner-lifecycle-watch");
		Directory.CreateDirectory(session.RootDirectory);
		session.Worker = new Thread((ThreadStart)delegate
		{
			string path = Path.Combine(session.RootDirectory, "lifecycle-timeline.csv");
			string path2 = Path.Combine(session.RootDirectory, "transient-live-diffs.csv");
			string path3 = Path.Combine(session.RootDirectory, "owner-live-diffs.csv");
			try
			{
				using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				using StreamWriter streamWriter2 = new StreamWriter(path2, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				using StreamWriter streamWriter3 = new StreamWriter(path3, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.WriteLine("elapsed_ms,event,pointer,value,detail");
				streamWriter2.WriteLine("elapsed_ms,region,offset_start,absolute_va,length,before_hex,after_hex");
				streamWriter3.WriteLine("elapsed_ms,region,offset_start,absolute_va,length,before_hex,after_hex");
				Stopwatch stopwatch = Stopwatch.StartNew();
				ulong num = 0uL;
				uint num2 = uint.MaxValue;
				byte[] array = null;
				ulong num3 = 0uL;
				int num4 = 0;
				ulong num5 = 0uL;
				ulong num6 = 0uL;
				byte[] array2 = null;
				while (!session.StopRequested)
				{
					long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
					if (!ReadU64(handle, manager + 432, out var value, out var error))
					{
						value = 0uL;
					}
					if (value != num)
					{
						string text = ((value == 0L) ? "TRANSIENT_CLEARED" : ((num == 0L) ? "TRANSIENT_BECAME_NONZERO" : "TRANSIENT_POINTER_CHANGED"));
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + "," + text + "," + HU(value) + ",,manager+0x1B0");
						streamWriter.Flush();
						num2 = uint.MaxValue;
						array = null;
						if (value != 0L)
						{
							num3 = value;
							num4++;
							session.TransientGenerations = num4;
							string text2 = Path.Combine(session.RootDirectory, "transient_generation_" + num4.ToString("D2", CultureInfo.InvariantCulture));
							Directory.CreateDirectory(text2);
							DumpImmediateObjectSnapshot(handle, text2, "transient_FIRST_0x1340", value, 4928);
							byte[] array3 = Read(handle, (long)value, 4928, out error);
							if (array3.Length >= 8)
							{
								ulong num7 = BitConverter.ToUInt64(array3, 0);
								if (num7 != 0L)
								{
									DumpImmediateObjectSnapshot(handle, text2, "transient_vtable_FIRST_0x400", num7, 1024);
								}
							}
							array = array3;
						}
					}
					if (value != 0L)
					{
						if (!ReadU32(handle, (long)(value + 168), out var value2, out error))
						{
							value2 = uint.MaxValue;
						}
						if (value2 != num2)
						{
							streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",TRANSIENT_STATE," + HU(value) + "," + value2.ToString(CultureInfo.InvariantCulture) + ",offset+0xA8");
							streamWriter.Flush();
							num2 = value2;
							session.LastTransientState = value2;
						}
						byte[] array4 = Read(handle, (long)value, 512, out error);
						if (array != null && array4.Length != 0)
						{
							WriteDiffRows(streamWriter2, elapsedMilliseconds, "TransientObject", (long)value, array, array4);
						}
						if (array4.Length != 0)
						{
							array = array4;
						}
					}
					num = value;
					session.LastTransientPointer = value;
					ulong num8 = ((DataU64(handle, ctorHook, 0) != 0L) ? DataU64(handle, ctorHook, 16) : 0);
					if (num5 == 0L && num8 != 0L)
					{
						num5 = num8;
						session.OwnerSeen = true;
						session.OwnerPointer = num5;
						session.OwnerFirstSeenMs = elapsedMilliseconds;
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",OWNER_SEEN," + HU(num5) + ",,constructor RCX");
						streamWriter.Flush();
						string text3 = Path.Combine(session.RootDirectory, "owner_" + num5.ToString("X", CultureInfo.InvariantCulture));
						Directory.CreateDirectory(text3);
						DumpImmediateObjectSnapshot(handle, text3, "owner_FIRST_0x360", num5, 864);
						array2 = Read(handle, (long)num5, 864, out error);
						if (array2.Length >= 8)
						{
							num6 = BitConverter.ToUInt64(array2, 0);
							session.OwnerFirstVtable = num6;
							session.OwnerLastVtable = num6;
							streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",OWNER_VTABLE," + HU(num5) + "," + HU(num6) + ",first observed");
							streamWriter.Flush();
						}
					}
					else if (num5 != 0L)
					{
						byte[] array5 = Read(handle, (long)num5, 864, out error);
						if (array5.Length >= 8)
						{
							ulong num9 = BitConverter.ToUInt64(array5, 0);
							session.OwnerLastVtable = num9;
							session.OwnerLastSeenMs = elapsedMilliseconds;
							if (num9 != num6)
							{
								session.OwnerVtableChanges++;
								streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",OWNER_VTABLE_CHANGE," + HU(num5) + "," + HU(num9) + ",previous=" + HU(num6));
								streamWriter.Flush();
								num6 = num9;
							}
							if (array2 != null)
							{
								WriteDiffRows(streamWriter3, elapsedMilliseconds, "OwnerObject", (long)num5, array2, array5);
							}
							array2 = array5;
						}
						else
						{
							streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",OWNER_UNREADABLE," + HU(num5) + ",,ReadProcessMemory returned " + array5.Length.ToString(CultureInfo.InvariantCulture) + " bytes");
							streamWriter.Flush();
						}
					}
					Thread.Sleep(5);
				}
				if (num3 != 0L)
				{
					string text4 = Path.Combine(session.RootDirectory, "transient_final");
					Directory.CreateDirectory(text4);
					DumpImmediateObjectSnapshot(handle, text4, "transient_FINAL_0x1340", num3, 4928);
				}
				if (num5 != 0L)
				{
					string text5 = Path.Combine(session.RootDirectory, "owner_final");
					Directory.CreateDirectory(text5);
					DumpImmediateObjectSnapshot(handle, text5, "owner_FINAL_0x360", num5, 864);
				}
			}
			catch (Exception ex)
			{
				try
				{
					File.WriteAllText(Path.Combine(session.RootDirectory, "watch-error.txt"), ex.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				}
				catch
				{
				}
			}
		});
		session.Worker.IsBackground = true;
		session.Worker.Start();
		return session;
	}

	private static void StopLifecycleWatch(LifecycleWatchSession session)
	{
		if (session != null)
		{
			session.StopRequested = true;
			if (session.Worker != null && session.Worker.IsAlive)
			{
				session.Worker.Join(3000);
			}
		}
	}

	private static string ReadEaSmallString(IntPtr handle, ulong address, out int length)
	{
		length = 0;
		if (address < 65536)
		{
			return "";
		}
		int error;
		byte[] array = Read(handle, (long)address, 24, out error);
		if (array.Length < 16)
		{
			return "";
		}
		sbyte b = (sbyte)array[15];
		ulong num = address;
		if ((b & -128) != 0)
		{
			if (array.Length < 12)
			{
				return "";
			}
			num = BitConverter.ToUInt64(array, 0);
			length = BitConverter.ToInt32(array, 8);
		}
		else
		{
			length = 15 - b;
		}
		if (length < 0)
		{
			length = 0;
		}
		if (length > 4096)
		{
			length = 4096;
		}
		if (num < 65536 || length == 0)
		{
			return "";
		}
		byte[] array2 = Read(handle, (long)num, length, out error);
		if (array2.Length == 0)
		{
			return "";
		}
		int count = Math.Min(length, array2.Length);
		return Encoding.ASCII.GetString(array2, 0, count);
	}

	private static string ExtractHeaderLine(string header, string prefix)
	{
		if (string.IsNullOrEmpty(header))
		{
			return "";
		}
		string[] array = header.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return array[i].Substring(prefix.Length).Trim();
			}
		}
		return "";
	}

	private static int ExtractBearerLength(string header)
	{
		string text = ExtractHeaderLine(header, "Authorization:");
		if (text.Equals("Bearer", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (text.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
		{
			return Math.Max(0, text.Length - "Bearer ".Length);
		}
		return text.Length;
	}

	private static string RedactAuthorizationHeader(string header)
	{
		if (string.IsNullOrEmpty(header))
		{
			return header ?? "";
		}
		string[] array = header.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
		for (int i = 0; i < array.Length; i++)
		{
			if (array[i].StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
			{
				array[i] = "Authorization: Bearer <redacted:" + ExtractBearerLength(array[i]).ToString(CultureInfo.InvariantCulture) + " bytes>";
			}
		}
		return string.Join("\r\n", array);
	}

	private static LocalMcrService StartLoopbackServer(string outputDirectory, out string errorText)
	{
		errorText = "";
		LocalMcrService localMcrService = null;
		try
		{
			RepairOptions repairOptions = new RepairOptions();
			string path = Path.Combine(Path.GetDirectoryName(typeof(CFB27TeamBuilderBatchQueuePreMcrProbe).Assembly.Location), OfflineTeamBuilderRuntime.RuntimePaths.OptionsFileName);
			if (File.Exists(path))
			{
				repairOptions = RepairJson.Decode<RepairOptions>(File.ReadAllBytes(path));
			}
			if (string.IsNullOrWhiteSpace(repairOptions.StoreDirectory))
			{
				repairOptions.StoreDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OfflineTeamBuilder", "local-assets-v111");
			}
			localMcrService = new LocalMcrService(outputDirectory, repairOptions);
			localMcrService.Start();
			B("LOCAL_ASSET_STORE " + localMcrService.StoreDirectory);
			return localMcrService;
		}
		catch (Exception ex)
		{
			localMcrService?.Dispose();
			errorText = ex.GetType().Name + ": " + ex.Message;
			return null;
		}
	}

	private static void StopLoopbackServer(LocalMcrService session)
	{
		session?.Stop();
	}

	private static void WriteLoopbackServerSummary(string directory, LocalMcrService session)
	{
		session?.WriteSummary(directory);
	}

	private static void WriteLocalUrlShimSummary(IntPtr handle, string directory, HookSpec hook)
	{
		string path = Path.Combine(directory, hook.Name + "_summary.txt");
		ulong num = DataU64(handle, hook, 0);
		ulong num2 = DataU64(handle, hook, 8);
		ulong num3 = DataU64(handle, hook, 16);
		ulong num4 = DataU64(handle, hook, 24);
		ulong num5 = DataU64(handle, hook, 32);
		ulong value = DataU64(handle, hook, 40);
		ulong num6 = DataU64(handle, hook, 48);
		int error;
		string text = ((num2 >= 65536) ? ReadAsciiZ(handle, (long)num2, 512, out error) : "");
		string text2 = ((num4 >= 65536) ? ReadAsciiZ(handle, (long)num4, 512, out error) : "");
		string text3 = ((num5 >= 65536) ? ReadAsciiZ(handle, (long)num5, 512, out error) : "");
		using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Hook: " + hook.Name);
		streamWriter.WriteLine("ProtoHttpRequest total calls: " + num.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("/api/ upload URLs redirected: " + num3.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Saved TeamBuilder locator URLs redirected: " + DataU64(handle, hook, 56).ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last original URL pointer: " + HU(num2));
		streamWriter.WriteLine("Last original URL text: " + text);
		streamWriter.WriteLine("Matched original URL pointer: " + HU(num4));
		streamWriter.WriteLine("Matched original URL text: " + text2);
		streamWriter.WriteLine("Effective loopback URL pointer: " + HU(num5));
		streamWriter.WriteLine("Effective loopback URL: " + text3);
		streamWriter.WriteLine("Last ProtoHttpRequest R8/body pointer: " + HU(value));
		streamWriter.WriteLine("Last ProtoHttpRequest R9/body length: " + num6.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Scope rule: only /api/ upload URLs and 0/<id>/<same-id> saved locators are redirected; all other ProtoHttp traffic remains stock.");
	}

	private static void DumpKnownMcrEndpointStrings(IntPtr handle, string directory, long moduleBase)
	{
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "KNOWN_MCR_ENDPOINT_STRINGS_FROM_GAME.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("Known MCR environment endpoint strings present in the game executable.");
		streamWriter.WriteLine("Evidence/reference only. v0.88 does NOT connect to EA MCR endpoints.");
		streamWriter.WriteLine();
		long[] array = new long[6] { 182779232L, 182779560L, 182780384L, 182780488L, 182781304L, 182781456L };
		string[] array2 = new string[6] { "MCR API INT", "MCR QUERY INT", "MCR API PROD", "MCR QUERY PROD", "MCR API LOAD", "MCR QUERY LOAD" };
		for (int i = 0; i < array.Length; i++)
		{
			int error;
			string text = ReadAsciiZ(handle, moduleBase + array[i], 256, out error);
			streamWriter.WriteLine(array2[i] + " RVA=0x" + array[i].ToString("X", CultureInfo.InvariantCulture) + " value=" + text);
		}
	}

	private static void DumpServiceConfigKeyTable(IntPtr handle, string directory, long moduleBase)
	{
		using (StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "SERVICE_CONFIG_KEY_TABLE_A8_B4.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
		{
			streamWriter.WriteLine("Runtime service-config key pointer table");
			streamWriter.WriteLine("Table RVA: 0x" + 226020016L.ToString("X", CultureInfo.InvariantCulture));
			streamWriter.WriteLine("The MCR path uses IDs 0xAC (MCR_ENABLED), 0xAE (MCR_API_HOST), 0xAF (MCR_QUERY_HOST), and 0xB0-0xB3 (category-specific MCR SID integer lookups). The bearer token is NOT service ID 0xAF; it comes from a separate dependency-root virtual accessor (+0x400).");
			streamWriter.WriteLine();
			streamWriter.WriteLine("id,pointer,ascii");
			for (int i = 168; i <= 180; i++)
			{
				string text = "";
				if (ReadU64(handle, moduleBase + 226020016 + (long)i * 8L, out var value, out var error) && value >= 65536 && Readable(handle, (long)value))
				{
					text = ReadAsciiZ(handle, (long)value, 512, out error);
					text = text.Replace("\r", "\\r").Replace("\n", "\\n");
				}
				else
				{
					value = 0uL;
				}
				streamWriter.WriteLine("0x" + i.ToString("X2", CultureInfo.InvariantCulture) + "," + HU(value) + "," + text);
			}
		}
		Dump(handle, directory, "RVA_D78CAB0_service_config_key_pointer_table", moduleBase + 226020016, 2336);
	}

	private static ProtoHttpWatchSession StartProtoHttpWatch(IntPtr handle, string outputDirectory, HookSpec ctorHook, HookSpec recvHook)
	{
		ProtoHttpWatchSession session = new ProtoHttpWatchSession();
		session.StopRequested = false;
		session.RootDirectory = Path.Combine(outputDirectory, "protohttp-watch");
		Directory.CreateDirectory(session.RootDirectory);
		session.Worker = new Thread((ThreadStart)delegate
		{
			string path = Path.Combine(session.RootDirectory, "protohttp-timeline.csv");
			try
			{
				using StreamWriter streamWriter = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				streamWriter.WriteLine("elapsed_ms,event,owner,protohttp,result,mode,recv_offset,flag_11ef,backend08,backend10,field20,detail");
				Stopwatch stopwatch = Stopwatch.StartNew();
				ulong num = 0uL;
				uint num2 = uint.MaxValue;
				int num3 = int.MinValue;
				int num4 = -1;
				ulong num5 = 0uL;
				while (!session.StopRequested)
				{
					long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
					ulong num6 = DataU64(handle, ctorHook, 16);
					if (num6 != 0L)
					{
						session.OwnerPointer = num6;
					}
					ulong value = 0uL;
					int error;
					if (num6 != 0L)
					{
						ReadU64(handle, (long)(num6 + 32), out value, out error);
					}
					if (value != num)
					{
						session.HandleChanges++;
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",PROTOHTTP_HANDLE," + HU(num6) + "," + HU(value) + ",,,,,,,,previous=" + HU(num));
						streamWriter.Flush();
						num = value;
						num2 = uint.MaxValue;
						num3 = int.MinValue;
						num4 = -1;
						if (value != 0L)
						{
							session.HandleSeen = true;
							session.HandlePointer = value;
							string text = Path.Combine(session.RootDirectory, "protohttp_" + value.ToString("X", CultureInfo.InvariantCulture));
							Directory.CreateDirectory(text);
							if (num6 != 0L)
							{
								int length;
								string text2 = ReadEaSmallString(handle, num6 + 840, out length);
								int length2;
								string text3 = ReadEaSmallString(handle, num6 + 864, out length2);
								session.BaseUrlValue = text2;
								session.BaseUrlLength = length;
								session.QueryHostValue = text3;
								session.QueryHostLength = length2;
								session.EndpointFieldsCaptured = true;
								streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",MCR_CONFIG_FIELDS," + HU(num6) + "," + HU(value) + ",,,,,,,,base_url_len=" + length.ToString(CultureInfo.InvariantCulture) + ";base_url=" + text2.Replace(",", "<comma>") + ";query_host_len=" + length2.ToString(CultureInfo.InvariantCulture) + ";query_host=" + text3.Replace(",", "<comma>"));
								streamWriter.Flush();
								DumpImmediateObjectSnapshot(handle, text, "owner_MCR_base_url_field_plus_0x340_0x20", num6 + 832, 32);
							}
							int error2;
							string text4 = ReadAsciiZ(handle, (long)(value + 1372), 1024, out error2);
							if (!string.IsNullOrEmpty(text4) && (text4.StartsWith("POST ", StringComparison.Ordinal) || text4.StartsWith("GET ", StringComparison.Ordinal) || text4.StartsWith("PUT ", StringComparison.Ordinal) || text4.StartsWith("DELETE ", StringComparison.Ordinal)))
							{
								string[] array = text4.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
								session.RequestLine = ((array.Length != 0) ? array[0] : "");
								session.HostHeader = ExtractHeaderLine(text4, "Host:");
								session.AuthorizationBearerLength = ExtractBearerLength(text4);
								session.RequestHeaderCaptured = true;
								File.WriteAllText(Path.Combine(text, "request_header_FIRST_REDACTED.txt"), RedactAuthorizationHeader(text4), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
								streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",REQUEST_HEADER," + HU(num6) + "," + HU(value) + ",,,,,,,,request=" + session.RequestLine.Replace(",", "<comma>") + ";host=" + session.HostHeader.Replace(",", "<comma>") + ";bearer_len=" + session.AuthorizationBearerLength.ToString(CultureInfo.InvariantCulture));
								streamWriter.Flush();
							}
						}
					}
					if (value != 0L)
					{
						if (!session.RequestHeaderCaptured)
						{
							int error3;
							string text5 = ReadAsciiZ(handle, (long)(value + 1372), 1024, out error3);
							if (!string.IsNullOrEmpty(text5) && (text5.StartsWith("POST ", StringComparison.Ordinal) || text5.StartsWith("GET ", StringComparison.Ordinal) || text5.StartsWith("PUT ", StringComparison.Ordinal) || text5.StartsWith("DELETE ", StringComparison.Ordinal)))
							{
								string[] array2 = text5.Split(new string[2] { "\r\n", "\n" }, StringSplitOptions.None);
								session.RequestLine = ((array2.Length != 0) ? array2[0] : "");
								session.HostHeader = ExtractHeaderLine(text5, "Host:");
								session.AuthorizationBearerLength = ExtractBearerLength(text5);
								session.RequestHeaderCaptured = true;
								string text6 = Path.Combine(session.RootDirectory, "protohttp_" + value.ToString("X", CultureInfo.InvariantCulture));
								Directory.CreateDirectory(text6);
								File.WriteAllText(Path.Combine(text6, "request_header_OBSERVED_REDACTED.txt"), RedactAuthorizationHeader(text5), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
								streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",REQUEST_HEADER_OBSERVED," + HU(num6) + "," + HU(value) + ",,,,,,,,request=" + session.RequestLine.Replace(",", "<comma>") + ";host=" + session.HostHeader.Replace(",", "<comma>") + ";bearer_len=" + session.AuthorizationBearerLength.ToString(CultureInfo.InvariantCulture));
								streamWriter.Flush();
							}
						}
						if (!ReadU32(handle, (long)(value + 180), out var value2, out error))
						{
							value2 = uint.MaxValue;
						}
						if (!ReadU32(handle, (long)(value + 232), out var value3, out error))
						{
							value3 = uint.MaxValue;
						}
						int num7 = (int)value3;
						byte b = byte.MaxValue;
						byte[] array3 = Read(handle, (long)(value + 4591), 1, out error);
						if (array3.Length == 1)
						{
							b = array3[0];
						}
						ulong value4 = 0uL;
						ulong value5 = 0uL;
						ulong value6 = 0uL;
						ReadU64(handle, (long)(value + 8), out value4, out error);
						ReadU64(handle, (long)(value + 16), out value5, out error);
						ReadU64(handle, (long)(value + 32), out value6, out error);
						session.LastMode = value2;
						session.LastRecvOffset = num7;
						session.LastFlag11EF = b;
						session.Backend08 = value4;
						session.Backend10 = value5;
						session.Field20 = value6;
						if (!session.FirstStateCaptured)
						{
							session.FirstStateCaptured = true;
							session.FirstMode = value2;
							session.FirstRecvOffset = num7;
							session.FirstFlag11EF = b;
						}
						if (value2 != num2 || num7 != num3 || b != num4)
						{
							streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",PROTOHTTP_STATE," + HU(num6) + "," + HU(value) + ",," + value2.ToString(CultureInfo.InvariantCulture) + "," + num7.ToString(CultureInfo.InvariantCulture) + "," + b.ToString(CultureInfo.InvariantCulture) + "," + HU(value4) + "," + HU(value5) + "," + HU(value6) + ",");
							streamWriter.Flush();
							num2 = value2;
							num3 = num7;
							num4 = b;
						}
					}
					ulong num8 = DataU64(handle, recvHook, 0);
					if (num8 != num5)
					{
						int num9 = (int)DataU64(handle, recvHook, 8);
						session.LastRecvCallCount = num8;
						session.LastRecvResult = num9;
						if (num9 == -3)
						{
							session.RecvWaitSeen = true;
						}
						if (num9 == -2)
						{
							session.RecvFailSeen = true;
						}
						streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",RECV_RESULT," + HU(num6) + "," + HU(value) + "," + num9.ToString(CultureInfo.InvariantCulture) + "," + session.LastMode.ToString(CultureInfo.InvariantCulture) + "," + session.LastRecvOffset.ToString(CultureInfo.InvariantCulture) + "," + session.LastFlag11EF.ToString(CultureInfo.InvariantCulture) + "," + HU(session.Backend08) + "," + HU(session.Backend10) + "," + HU(session.Field20) + ",calls=" + num8.ToString(CultureInfo.InvariantCulture));
						streamWriter.Flush();
						if (num9 == -2 && value != 0L)
						{
							string text7 = Path.Combine(session.RootDirectory, "protohttp_" + value.ToString("X", CultureInfo.InvariantCulture));
							Directory.CreateDirectory(text7);
							if (ReadU32(handle, (long)(value + 184), out var value7, out error))
							{
								session.ErrorCodeB8 = (int)value7;
							}
							if (ReadU32(handle, (long)(value + 188), out var value8, out error))
							{
								session.ErrorCodeBC = value8;
							}
							session.ErrorText = ReadAsciiZ(handle, (long)(value + 4444), 256, out var _);
							File.WriteAllText(Path.Combine(text7, "recvfail_decoded.txt"), "mode=" + session.LastMode.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + "error_dword_B8_signed=" + session.ErrorCodeB8.ToString(CultureInfo.InvariantCulture) + Environment.NewLine + "error_dword_BC=0x" + session.ErrorCodeBC.ToString("X8", CultureInfo.InvariantCulture) + Environment.NewLine + "error_text=" + session.ErrorText + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
							streamWriter.WriteLine(elapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + ",RECVFAIL_DECODED," + HU(num6) + "," + HU(value) + "," + num9.ToString(CultureInfo.InvariantCulture) + "," + session.LastMode.ToString(CultureInfo.InvariantCulture) + ",,,,,,,error_B8=" + session.ErrorCodeB8.ToString(CultureInfo.InvariantCulture) + ";error_BC=0x" + session.ErrorCodeBC.ToString("X8", CultureInfo.InvariantCulture) + ";text=" + session.ErrorText.Replace(",", "<comma>"));
							streamWriter.Flush();
							if (session.Backend08 != 0L)
							{
								DumpImmediateObjectSnapshot(handle, text7, "backend08_AT_RECVFAIL_0x800", session.Backend08, 2048);
							}
							if (session.Backend10 != 0L)
							{
								DumpImmediateObjectSnapshot(handle, text7, "backend10_AT_RECVFAIL_0x800", session.Backend10, 2048);
							}
							if (session.Field20 != 0L)
							{
								DumpImmediateObjectSnapshot(handle, text7, "field20_AT_RECVFAIL_0x800", session.Field20, 2048);
							}
						}
						num5 = num8;
					}
					Thread.Sleep(2);
				}
				if (session.HandlePointer != 0L)
				{
					Directory.CreateDirectory(Path.Combine(session.RootDirectory, "protohttp_" + session.HandlePointer.ToString("X", CultureInfo.InvariantCulture)));
				}
			}
			catch (Exception ex)
			{
				try
				{
					File.WriteAllText(Path.Combine(session.RootDirectory, "watch-error.txt"), ex.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
				}
				catch
				{
				}
			}
		});
		session.Worker.IsBackground = true;
		session.Worker.Start();
		return session;
	}

	private static void StopProtoHttpWatch(ProtoHttpWatchSession session)
	{
		if (session != null)
		{
			session.StopRequested = true;
			if (session.Worker != null && session.Worker.IsAlive)
			{
				session.Worker.Join(3000);
			}
		}
	}

	private static void WriteProtoHttpWatchSummary(string directory, ProtoHttpWatchSession session)
	{
		using StreamWriter streamWriter = new StreamWriter(Path.Combine(directory, "PROTOHTTP_RECVFAIL_CONTEXT_summary.txt"), append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		streamWriter.WriteLine("ProtoHttp handle seen: " + session.HandleSeen);
		streamWriter.WriteLine("Owner pointer: " + HU(session.OwnerPointer));
		streamWriter.WriteLine("ProtoHttp wrapper pointer: " + HU(session.HandlePointer));
		streamWriter.WriteLine("Handle changes: " + session.HandleChanges.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("First mode/type [handle+0xB4]: " + session.FirstMode.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last mode/type [handle+0xB4]: " + session.LastMode.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("First recv offset [handle+0xE8]: " + session.FirstRecvOffset.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last recv offset [handle+0xE8]: " + session.LastRecvOffset.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("First flag [handle+0x11EF]: " + session.FirstFlag11EF.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last flag [handle+0x11EF]: " + session.LastFlag11EF.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Backend +0x08: " + HU(session.Backend08));
		streamWriter.WriteLine("Backend +0x10: " + HU(session.Backend10));
		streamWriter.WriteLine("Field +0x20: " + HU(session.Field20));
		streamWriter.WriteLine("Recv calls observed: " + session.LastRecvCallCount.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("Last recv result: " + session.LastRecvResult.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("PROTOHTTP_RECVWAIT (-3) observed: " + session.RecvWaitSeen);
		streamWriter.WriteLine("PROTOHTTP_RECVFAIL (-2) observed: " + session.RecvFailSeen);
		streamWriter.WriteLine("MCR base URL field captured: " + session.EndpointFieldsCaptured);
		streamWriter.WriteLine("MCR base URL length: " + session.BaseUrlLength.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("MCR base URL value: " + session.BaseUrlValue);
		streamWriter.WriteLine("MCR query-host length [owner+0x360 / service ID 0xAF]: " + session.QueryHostLength.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("MCR query-host value: " + session.QueryHostValue);
		streamWriter.WriteLine("Request header captured: " + session.RequestHeaderCaptured);
		streamWriter.WriteLine("Request line: " + session.RequestLine);
		streamWriter.WriteLine("Host header value: " + session.HostHeader);
		streamWriter.WriteLine("Authorization Bearer payload length (content not logged): " + session.AuthorizationBearerLength.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("ProtoHttp failure mode +0xB4: " + session.LastMode.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("ProtoHttp error dword +0xB8 signed: " + session.ErrorCodeB8.ToString(CultureInfo.InvariantCulture));
		streamWriter.WriteLine("ProtoHttp error dword +0xBC: 0x" + session.ErrorCodeBC.ToString("X8", CultureInfo.InvariantCulture));
		streamWriter.WriteLine("ProtoHttp error text +0x115C: " + session.ErrorText);
		streamWriter.WriteLine();
		streamWriter.WriteLine("DirtySDK constants: RECVDONE=-1, RECVFAIL=-2, RECVWAIT=-3, RECVHEAD=-4, RECVBUFF=-5.");
	}

	internal static void RequestStop()
	{
		PersistentCancelRequested = true;
	}

	private static bool RestoreHook(IntPtr handle, HookSpec spec, out int error)
	{
		error = 0;
		if (spec == null || spec.Original == null || spec.Original.Length == 0 || spec.ResolvedTarget == 0L)
		{
			return true;
		}
		byte[] array = Read(handle, spec.ResolvedTarget, spec.Original.Length, out error);
		if (array.Length != spec.Original.Length)
		{
			return false;
		}
		if (ExactMatches(array, 0, spec.Original))
		{
			return true;
		}
		byte[] array2 = InstalledHookPatch(spec);
		if (array2.Length != array.Length || !ExactMatches(array, 0, array2))
		{
			error = 31001;
			B("RESTORE SKIPPED: target no longer contains this plugin's patch: " + spec.Name);
			return false;
		}
		if (!VirtualProtectEx(handle, new IntPtr(spec.ResolvedTarget), (UIntPtr)(ulong)spec.Original.Length, 64u, out var oldProtect))
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		bool result = Write(handle, spec.ResolvedTarget, spec.Original, out error);
		FlushInstructionCache(handle, new IntPtr(spec.ResolvedTarget), (UIntPtr)(ulong)spec.Original.Length);
		VirtualProtectEx(handle, new IntPtr(spec.ResolvedTarget), (UIntPtr)(ulong)spec.Original.Length, oldProtect, out var _);
		return result;
	}

	private static Process WaitForCollegeFootballProcess()
	{
		Console.WriteLine("Waiting for CollegeFB27.exe. You may launch the game normally now.");
		while (!PersistentCancelRequested)
		{
			Process[] processesByName = Process.GetProcessesByName("CollegeFB27");
			if (processesByName.Length != 0)
			{
				return processesByName[0];
			}
			Thread.Sleep(1000);
		}
		return null;
	}

	private static bool TryContextFromGlobal(IntPtr handle, long moduleBase, long globalRva, out long ultimateCareer, out long manager, out long config, out uint capacity, out uint count)
	{
		ultimateCareer = 0L;
		manager = 0L;
		config = 0L;
		capacity = 0u;
		count = 0u;
		if (!ReadU64(handle, moduleBase + globalRva, out var value, out var error) || value == 0L)
		{
			return false;
		}
		if (!ReadU64(handle, (long)value + (long)ResolvedManagerOffset, out var value2, out error) || value2 == 0L)
		{
			return false;
		}
		if (!ReadU64(handle, (long)value2 + (long)ResolvedConfigOffset, out var value3, out error) || value3 == 0L)
		{
			return false;
		}
		if (!ReadU32(handle, (long)value3 + (long)ResolvedCapacityOffset, out var value4, out error))
		{
			return false;
		}
		if (value4 != 0 && value4 != 16)
		{
			return false;
		}
		if (!ReadU32(handle, (long)(value + 2008), out var value5, out error))
		{
			value5 = uint.MaxValue;
		}
		ultimateCareer = (long)value;
		manager = (long)value2;
		config = (long)value3;
		capacity = value4;
		count = value5;
		return true;
	}

	private static bool PlausibleObjectPointer(ulong value, long moduleBase, int moduleSize)
	{
		if (value < 65536 || value >= 140737488355328L)
		{
			return false;
		}
		if ((value & 7) != 0L)
		{
			return false;
		}
		return true;
	}

	private static bool TryScanContextGlobalWindow(IntPtr handle, long moduleBase, int moduleSize, long centerRva, long radius, out long foundRva, out long ultimateCareer, out long manager, out long config, out uint capacity, out uint count)
	{
		foundRva = 0L;
		ultimateCareer = (manager = (config = 0L));
		capacity = (count = 0u);
		long num = Math.Max(0L, centerRva - radius);
		long num2 = Math.Min((long)moduleSize - 8L, centerRva + radius);
		long num3 = moduleBase + num;
		long num4 = moduleBase + num2;
		long num5 = num3;
		int error = 0;
		int num6 = 0;
		long num7 = 0L;
		long num8 = 0L;
		long num9 = 0L;
		long num10 = 0L;
		uint num11 = 0u;
		uint num12 = 0u;
		MBI64 mbi;
		while (num5 <= num4 && Query(handle, num5, out mbi))
		{
			long baseAddress = (long)mbi.BaseAddress;
			long num13;
			try
			{
				num13 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num13 = num4 + 1;
			}
			if (num13 <= num5)
			{
				num13 = num5 + 4096;
			}
			long num14 = Math.Max(num5, baseAddress);
			long num15 = Math.Min(num4 + 1, num13);
			if (mbi.State == 4096 && IsReadableProtection(mbi.Protect) && num15 > num14)
			{
				int num17;
				for (long num16 = num14; num16 < num15; num16 += num17)
				{
					num17 = (int)Math.Min(1048576L, num15 - num16);
					byte[] array = Read(handle, num16, num17, out error);
					for (int i = (int)((8 - ((num16 - moduleBase) & 7)) & 7); i + 8 <= array.Length; i += 8)
					{
						if (!PlausibleObjectPointer(BitConverter.ToUInt64(array, i), moduleBase, moduleSize))
						{
							continue;
						}
						long num18 = num16 + i - moduleBase;
						if (!TryContextFromGlobal(handle, moduleBase, num18, out var ultimateCareer2, out var manager2, out var config2, out var capacity2, out var count2))
						{
							continue;
						}
						if (num6 == 0)
						{
							num6 = 1;
							num7 = num18;
							num8 = ultimateCareer2;
							num9 = manager2;
							num10 = config2;
							num11 = capacity2;
							num12 = count2;
						}
						else
						{
							if (ultimateCareer2 != num8 || manager2 != num9 || config2 != num10)
							{
								return false;
							}
							long num19 = Math.Abs(num7 - centerRva);
							if (Math.Abs(num18 - centerRva) < num19)
							{
								num7 = num18;
							}
						}
					}
				}
			}
			num5 = Math.Max(num5 + 4096, num13);
		}
		if (num6 == 1)
		{
			foundRva = num7;
			ultimateCareer = num8;
			manager = num9;
			config = num10;
			capacity = num11;
			count = num12;
			return true;
		}
		return false;
	}

	private static bool LooksLikeModuleVtableObject(IntPtr handle, long objectAddress, long moduleBase, int moduleSize)
	{
		if (objectAddress < 65536 || objectAddress >= 140737488355328L)
		{
			return false;
		}
		if (!ReadU64(handle, objectAddress, out var value, out var _))
		{
			return false;
		}
		ulong num = (ulong)(moduleBase + moduleSize);
		if (value >= (ulong)moduleBase)
		{
			return value < num;
		}
		return false;
	}

	private static bool TryContextAtOffsets(IntPtr handle, long moduleBase, long globalRva, int managerOffset, int configOffset, int capacityOffset, out long ultimateCareer, out long manager, out long config, out uint capacity, out uint count)
	{
		ultimateCareer = (manager = (config = 0L));
		capacity = (count = 0u);
		if (!ReadU64(handle, moduleBase + globalRva, out var value, out var error) || value == 0L)
		{
			return false;
		}
		if (!ReadU64(handle, (long)value + (long)managerOffset, out var value2, out error) || value2 == 0L)
		{
			return false;
		}
		if (!ReadU64(handle, (long)value2 + (long)configOffset, out var value3, out error) || value3 == 0L)
		{
			return false;
		}
		if (!ReadU32(handle, (long)value3 + (long)capacityOffset, out var value4, out error))
		{
			return false;
		}
		if (value4 != 0 && value4 != 16)
		{
			return false;
		}
		if (!ReadU32(handle, (long)(value + 2008), out var value5, out error))
		{
			value5 = uint.MaxValue;
		}
		ultimateCareer = (long)value;
		manager = (long)value2;
		config = (long)value3;
		capacity = value4;
		count = value5;
		return true;
	}

	private static void AddAdaptiveMatch(List<AdaptiveContextMatch> matches, AdaptiveContextMatch candidate)
	{
		for (int i = 0; i < matches.Count; i++)
		{
			AdaptiveContextMatch adaptiveContextMatch = matches[i];
			if (adaptiveContextMatch.UltimateCareer == candidate.UltimateCareer && adaptiveContextMatch.Manager == candidate.Manager && adaptiveContextMatch.Config == candidate.Config && adaptiveContextMatch.ManagerOffset == candidate.ManagerOffset && adaptiveContextMatch.ConfigOffset == candidate.ConfigOffset && adaptiveContextMatch.CapacityOffset == candidate.CapacityOffset)
			{
				if (Math.Abs(candidate.GlobalRva - (259972648 + StringRelocationDeltaHint)) < Math.Abs(adaptiveContextMatch.GlobalRva - (259972648 + StringRelocationDeltaHint)))
				{
					adaptiveContextMatch.GlobalRva = candidate.GlobalRva;
				}
				return;
			}
		}
		matches.Add(candidate);
	}

	private static bool TryAdaptiveContextFromGlobal(IntPtr handle, long moduleBase, int moduleSize, long globalRva, List<AdaptiveContextMatch> matches, ref long globalsWithObjectVtable, ref long managerObjectsSeen, ref long configObjectsSeen, ref long capacityValuesRead)
	{
		if (!ReadU64(handle, moduleBase + globalRva, out var value, out var error) || value == 0L)
		{
			return false;
		}
		if (!PlausibleObjectPointer(value, moduleBase, moduleSize))
		{
			return false;
		}
		long num = (long)value;
		if (!LooksLikeModuleVtableObject(handle, num, moduleBase, moduleSize))
		{
			return false;
		}
		globalsWithObjectVtable++;
		byte[] array = Read(handle, num + 872, 520, out error);
		if (array.Length == 520)
		{
			for (int i = 0; i + 8 <= array.Length; i += 8)
			{
				int num2 = 872 + i;
				if (num2 == 1128)
				{
					continue;
				}
				ulong num3 = BitConverter.ToUInt64(array, i);
				if (!PlausibleObjectPointer(num3, moduleBase, moduleSize))
				{
					continue;
				}
				long num4 = (long)num3;
				if (!LooksLikeModuleVtableObject(handle, num4, moduleBase, moduleSize))
				{
					continue;
				}
				managerObjectsSeen++;
				if (!ReadU64(handle, num4 + 488, out var value2, out error) || value2 == 0L)
				{
					continue;
				}
				configObjectsSeen++;
				if (!ReadU32(handle, (long)(value2 + 76), out var value3, out error))
				{
					continue;
				}
				capacityValuesRead++;
				if (value3 == 0 || value3 == 16)
				{
					if (!ReadU32(handle, num + 2008, out var value4, out error))
					{
						value4 = uint.MaxValue;
					}
					AddAdaptiveMatch(matches, new AdaptiveContextMatch
					{
						GlobalRva = globalRva,
						UltimateCareer = num,
						Manager = num4,
						Config = (long)value2,
						Capacity = value3,
						Count = value4,
						ManagerOffset = num2,
						ConfigOffset = 488,
						CapacityOffset = 76,
						Mode = "manager-offset"
					});
				}
			}
		}
		if (ReadU64(handle, num + 1128, out var value5, out error) && value5 != 0L && PlausibleObjectPointer(value5, moduleBase, moduleSize))
		{
			long num5 = (long)value5;
			if (LooksLikeModuleVtableObject(handle, num5, moduleBase, moduleSize))
			{
				managerObjectsSeen++;
				byte[] array2 = Read(handle, num5 + 232, 520, out error);
				if (array2.Length == 520)
				{
					for (int j = 0; j + 8 <= array2.Length; j += 8)
					{
						int num6 = 232 + j;
						if (num6 == 488)
						{
							continue;
						}
						ulong num7 = BitConverter.ToUInt64(array2, j);
						if (!PlausibleObjectPointer(num7, moduleBase, moduleSize))
						{
							continue;
						}
						configObjectsSeen++;
						if (!ReadU32(handle, (long)(num7 + 76), out var value6, out error))
						{
							continue;
						}
						capacityValuesRead++;
						if (value6 == 0 || value6 == 16)
						{
							if (!ReadU32(handle, num + 2008, out var value7, out error))
							{
								value7 = uint.MaxValue;
							}
							AddAdaptiveMatch(matches, new AdaptiveContextMatch
							{
								GlobalRva = globalRva,
								UltimateCareer = num,
								Manager = num5,
								Config = (long)num7,
								Capacity = value6,
								Count = value7,
								ManagerOffset = 1128,
								ConfigOffset = num6,
								CapacityOffset = 76,
								Mode = "config-offset"
							});
						}
					}
				}
				if (ReadU64(handle, num5 + 488, out var value8, out error) && value8 != 0L && PlausibleObjectPointer(value8, moduleBase, moduleSize))
				{
					long num8 = (long)value8;
					configObjectsSeen++;
					byte[] array3 = Read(handle, num8 + 12, 132, out error);
					if (array3.Length == 132)
					{
						for (int k = 0; k + 4 <= array3.Length; k += 4)
						{
							int num9 = 12 + k;
							if (num9 == 76)
							{
								continue;
							}
							uint num10 = BitConverter.ToUInt32(array3, k);
							capacityValuesRead++;
							if (num10 == 0 || num10 == 16)
							{
								if (!ReadU32(handle, num + 2008, out var value9, out error))
								{
									value9 = uint.MaxValue;
								}
								AddAdaptiveMatch(matches, new AdaptiveContextMatch
								{
									GlobalRva = globalRva,
									UltimateCareer = num,
									Manager = num5,
									Config = num8,
									Capacity = num10,
									Count = value9,
									ManagerOffset = 1128,
									ConfigOffset = 488,
									CapacityOffset = num9,
									Mode = "capacity-offset"
								});
							}
						}
					}
				}
			}
		}
		return matches.Count != 0;
	}

	private static bool TryScanAdaptiveContextWindow(IntPtr handle, long moduleBase, int moduleSize, long centerRva, long radius, out AdaptiveContextMatch match, out string diagnostic)
	{
		match = null;
		diagnostic = "";
		long num = Math.Max(0L, centerRva - radius);
		long num2 = Math.Min((long)moduleSize - 8L, centerRva + radius);
		List<AdaptiveContextMatch> list = new List<AdaptiveContextMatch>();
		long globalsWithObjectVtable = 0L;
		long managerObjectsSeen = 0L;
		long configObjectsSeen = 0L;
		long capacityValuesRead = 0L;
		long num3 = 0L;
		int error = 0;
		int num5;
		for (long num4 = num; num4 <= num2; num4 += num5)
		{
			num5 = (int)Math.Min(262144L, num2 - num4 + 1);
			byte[] array = Read(handle, moduleBase + num4, num5, out error);
			for (int i = (int)((8 - (num4 & 7)) & 7); i + 8 <= array.Length; i += 8)
			{
				num3++;
				if (PlausibleObjectPointer(BitConverter.ToUInt64(array, i), moduleBase, moduleSize))
				{
					long globalRva = num4 + i;
					TryAdaptiveContextFromGlobal(handle, moduleBase, moduleSize, globalRva, list, ref globalsWithObjectVtable, ref managerObjectsSeen, ref configObjectsSeen, ref capacityValuesRead);
					if (list.Count > 24)
					{
						break;
					}
				}
			}
			if (list.Count > 24)
			{
				break;
			}
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("center=0x").Append(centerRva.ToString("X", CultureInfo.InvariantCulture));
		stringBuilder.Append(" radius=0x").Append(radius.ToString("X", CultureInfo.InvariantCulture));
		stringBuilder.Append(" qwords=").Append(num3.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" globalsWithModuleVtable=").Append(globalsWithObjectVtable.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" managerObjects=").Append(managerObjectsSeen.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" configObjects=").Append(configObjectsSeen.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" capacityReads=").Append(capacityValuesRead.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" matches=").Append(list.Count.ToString(CultureInfo.InvariantCulture));
		for (int j = 0; j < list.Count && j < 8; j++)
		{
			AdaptiveContextMatch adaptiveContextMatch = list[j];
			stringBuilder.Append("; [").Append(j.ToString(CultureInfo.InvariantCulture)).Append("] global=0x")
				.Append(adaptiveContextMatch.GlobalRva.ToString("X", CultureInfo.InvariantCulture))
				.Append(" uc=")
				.Append(H(adaptiveContextMatch.UltimateCareer))
				.Append(" mgr=")
				.Append(H(adaptiveContextMatch.Manager))
				.Append(" cfg=")
				.Append(H(adaptiveContextMatch.Config))
				.Append(" offsets=+0x")
				.Append(adaptiveContextMatch.ManagerOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append("/+0x")
				.Append(adaptiveContextMatch.ConfigOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append("/+0x")
				.Append(adaptiveContextMatch.CapacityOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append(" cap=")
				.Append(adaptiveContextMatch.Capacity.ToString(CultureInfo.InvariantCulture))
				.Append(" mode=")
				.Append(adaptiveContextMatch.Mode);
		}
		diagnostic = stringBuilder.ToString();
		if (list.Count == 1)
		{
			match = list[0];
			return true;
		}
		return false;
	}

	private static bool IsExecutableProtection(uint protect)
	{
		uint num = protect & 0xFF;
		if (num != 16 && num != 32 && num != 64)
		{
			return num == 128;
		}
		return true;
	}

	private static int SignExtend8(byte value)
	{
		return (sbyte)value;
	}

	private static bool InContextGlobalNeighborhood(long rva, int moduleSize)
	{
		if (rva < 0 || rva >= moduleSize)
		{
			return false;
		}
		if (Math.Abs(rva - 259972648) <= 4194304)
		{
			return true;
		}
		long num = 259972648 + StringRelocationDeltaHint;
		if (num >= 0 && num < moduleSize && Math.Abs(rva - num) <= 4194304)
		{
			return true;
		}
		return false;
	}

	private static bool TryDecodeRipQwordLoad(byte[] code, int offset, long instructionVa, out int destReg, out long targetVa, out int length)
	{
		destReg = -1;
		targetVa = 0L;
		length = 0;
		if (code == null || offset < 0 || offset + 7 > code.Length)
		{
			return false;
		}
		byte b = code[offset];
		if ((b & 0xF8) != 72 || (b & 8) == 0)
		{
			return false;
		}
		if (code[offset + 1] != 139)
		{
			return false;
		}
		byte b2 = code[offset + 2];
		int num = (b2 >> 6) & 3;
		int num2 = b2 & 7;
		if (num != 0 || num2 != 5)
		{
			return false;
		}
		int num3 = (b2 >> 3) & 7;
		if ((b & 4) != 0)
		{
			num3 += 8;
		}
		int num4 = BitConverter.ToInt32(code, offset + 3);
		destReg = num3;
		length = 7;
		targetVa = instructionVa + length + num4;
		return true;
	}

	private static bool TryDecodeRegisterMemoryLoad(byte[] code, int offset, bool wantQword, out int destReg, out int baseReg, out int displacement, out int length)
	{
		destReg = (baseReg = -1);
		displacement = 0;
		length = 0;
		if (code == null || offset < 0 || offset + 3 > code.Length)
		{
			return false;
		}
		int num = offset;
		byte b = 0;
		if (code[num] >= 64 && code[num] <= 79)
		{
			b = code[num++];
			if (num + 2 > code.Length)
			{
				return false;
			}
		}
		if ((b & 8) != 0 != wantQword)
		{
			return false;
		}
		if (code[num++] != 139)
		{
			return false;
		}
		byte num2 = code[num++];
		int num3 = (num2 >> 6) & 3;
		int num4 = (num2 >> 3) & 7;
		int num5 = num2 & 7;
		if ((b & 4) != 0)
		{
			num4 += 8;
		}
		if (num3 == 3)
		{
			return false;
		}
		int num7;
		if (num5 == 4)
		{
			if (num >= code.Length)
			{
				return false;
			}
			byte b2 = code[num++];
			if (((b2 >> 3) & 7) != 4)
			{
				return false;
			}
			int num6 = b2 & 7;
			if (num3 == 0 && num6 == 5)
			{
				return false;
			}
			num7 = num6 + (((b & 1) != 0) ? 8 : 0);
		}
		else
		{
			if (num3 == 0 && num5 == 5)
			{
				return false;
			}
			num7 = num5 + (((b & 1) != 0) ? 8 : 0);
		}
		int num8 = 0;
		switch (num3)
		{
		case 1:
			if (num >= code.Length)
			{
				return false;
			}
			num8 = SignExtend8(code[num++]);
			break;
		case 2:
			if (num + 4 > code.Length)
			{
				return false;
			}
			num8 = BitConverter.ToInt32(code, num);
			num += 4;
			break;
		}
		destReg = num4;
		baseReg = num7;
		displacement = num8;
		length = num - offset;
		return true;
	}

	private static bool HasNearReturn(byte[] code, int afterOffset, int maxDistance)
	{
		if (code == null)
		{
			return false;
		}
		int num = Math.Min(code.Length, afterOffset + maxDistance);
		for (int i = afterOffset; i < num; i++)
		{
			if (code[i] == 195)
			{
				return true;
			}
			if (code[i] == 194 && i + 2 < code.Length)
			{
				return true;
			}
		}
		return false;
	}

	private static void AddCodeAccess(List<CodeCapacityAccess> accesses, CodeCapacityAccess candidate)
	{
		for (int i = 0; i < accesses.Count; i++)
		{
			CodeCapacityAccess codeCapacityAccess = accesses[i];
			if (codeCapacityAccess.GlobalRva == candidate.GlobalRva && codeCapacityAccess.ManagerOffset == candidate.ManagerOffset && codeCapacityAccess.ConfigOffset == candidate.ConfigOffset && codeCapacityAccess.CapacityOffset == candidate.CapacityOffset && codeCapacityAccess.GlobalLoadRva == candidate.GlobalLoadRva && codeCapacityAccess.CapacityLoadRva == candidate.CapacityLoadRva)
			{
				return;
			}
		}
		accesses.Add(candidate);
	}

	private static List<CodeCapacityAccess> FindCodeBackedCapacityAccesses(IntPtr handle, long moduleBase, int moduleSize, out Dictionary<long, int> globalXrefs, out string diagnostic)
	{
		List<CodeCapacityAccess> list = new List<CodeCapacityAccess>();
		globalXrefs = new Dictionary<long, int>();
		long num = moduleBase + moduleSize;
		long num2 = moduleBase;
		long num3 = 0L;
		long num4 = 0L;
		int error = 0;
		MBI64 mbi;
		while (num2 < num && list.Count < 512 && Query(handle, num2, out mbi))
		{
			long baseAddress = (long)mbi.BaseAddress;
			long num5;
			try
			{
				num5 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num5 = num;
			}
			if (num5 <= num2)
			{
				num5 = num2 + 4096;
			}
			long num6 = Math.Max(num2, baseAddress);
			long num7 = Math.Min(num, num5);
			if (mbi.State == 4096 && IsExecutableProtection(mbi.Protect) && num7 > num6)
			{
				int num9;
				for (long num8 = num6; num8 < num7; num8 += num9)
				{
					if (list.Count >= 512)
					{
						break;
					}
					int length = (int)Math.Min(1049120L, num7 - num8);
					byte[] array = Read(handle, num8, length, out error);
					if (array.Length == 0)
					{
						break;
					}
					num9 = (int)Math.Min(1048576L, num7 - num8);
					num3 += num9;
					for (int i = 0; i + 7 <= num9; i++)
					{
						if (!TryDecodeRipQwordLoad(array, i, num8 + i, out var destReg, out var targetVa, out var length2))
						{
							continue;
						}
						long num10 = targetVa - moduleBase;
						if (!InContextGlobalNeighborhood(num10, moduleSize))
						{
							continue;
						}
						num4++;
						if (!globalXrefs.TryGetValue(num10, out var value))
						{
							value = 0;
						}
						globalXrefs[num10] = value + 1;
						int num11 = i + length2;
						int num12 = Math.Min(array.Length - 3, num11 + 320);
						int num13 = 0;
						for (int j = num11; j <= num12; j++)
						{
							if (num13 >= 20)
							{
								break;
							}
							if (!TryDecodeRegisterMemoryLoad(array, j, wantQword: true, out var destReg2, out var baseReg, out var displacement, out var length3) || baseReg != destReg || displacement < 128 || displacement > 2560 || (displacement & 7) != 0)
							{
								continue;
							}
							num13++;
							int num14 = j + length3;
							int num15 = Math.Min(array.Length - 3, num14 + 288);
							int num16 = 0;
							for (int k = num14; k <= num15; k++)
							{
								if (num16 >= 16)
								{
									break;
								}
								if (!TryDecodeRegisterMemoryLoad(array, k, wantQword: true, out var destReg3, out var baseReg2, out var displacement2, out var length4) || baseReg2 != destReg2 || displacement2 < 8 || displacement2 > 1536 || (displacement2 & 7) != 0)
								{
									continue;
								}
								num16++;
								int num17 = k + length4;
								int num18 = Math.Min(array.Length - 3, num17 + 256);
								int num19 = 0;
								for (int l = num17; l <= num18; l++)
								{
									if (num19 >= 12)
									{
										break;
									}
									if (!TryDecodeRegisterMemoryLoad(array, l, wantQword: false, out var destReg4, out var baseReg3, out var displacement3, out var length5) || baseReg3 != destReg3 || destReg4 != 0 || displacement3 < 0 || displacement3 > 384 || (displacement3 & 3) != 0)
									{
										continue;
									}
									num19++;
									if (HasNearReturn(array, l + length5, 40))
									{
										AddCodeAccess(list, new CodeCapacityAccess
										{
											GlobalRva = num10,
											ManagerOffset = displacement,
											ConfigOffset = displacement2,
											CapacityOffset = displacement3,
											GlobalLoadRva = num8 + i - moduleBase,
											ManagerLoadRva = num8 + j - moduleBase,
											ConfigLoadRva = num8 + k - moduleBase,
											CapacityLoadRva = num8 + l - moduleBase,
											NearReturn = true
										});
										if (list.Count >= 512)
										{
											break;
										}
									}
								}
								if (list.Count >= 512)
								{
									break;
								}
							}
							if (list.Count >= 512)
							{
								break;
							}
						}
					}
				}
			}
			num2 = Math.Max(num2 + 4096, num5);
		}
		diagnostic = "executableBytes=" + num3.ToString(CultureInfo.InvariantCulture) + " RIP-global-loads-in-context-neighborhood=" + num4.ToString(CultureInfo.InvariantCulture) + " unique-context-globals-with-code-xrefs=" + globalXrefs.Count.ToString(CultureInfo.InvariantCulture) + " accessor-shaped-code-paths=" + list.Count.ToString(CultureInfo.InvariantCulture);
		return list;
	}

	private static bool TryResolveLiveCapacityFromCode(IntPtr handle, long moduleBase, int moduleSize, List<CodeCapacityAccess> accesses, Dictionary<long, int> globalXrefs, out LiveCapacityGroup winner, out string diagnostic)
	{
		winner = null;
		Dictionary<long, LiveCapacityGroup> dictionary = new Dictionary<long, LiveCapacityGroup>();
		int num = 0;
		int num2 = 0;
		int num3 = 0;
		int num4 = 0;
		for (int i = 0; i < accesses.Count; i++)
		{
			CodeCapacityAccess codeCapacityAccess = accesses[i];
			if (!ReadU64(handle, moduleBase + codeCapacityAccess.GlobalRva, out var value, out var error) || value == 0L)
			{
				continue;
			}
			long num5 = (long)value;
			if (!PlausibleObjectPointer(value, moduleBase, moduleSize) || !LooksLikeModuleVtableObject(handle, num5, moduleBase, moduleSize))
			{
				continue;
			}
			num++;
			if (!ReadU64(handle, num5 + codeCapacityAccess.ManagerOffset, out var value2, out error) || value2 == 0L)
			{
				continue;
			}
			long num6 = (long)value2;
			if (!PlausibleObjectPointer(value2, moduleBase, moduleSize) || !LooksLikeModuleVtableObject(handle, num6, moduleBase, moduleSize))
			{
				continue;
			}
			num2++;
			if (!ReadU64(handle, num6 + codeCapacityAccess.ConfigOffset, out var value3, out error) || value3 == 0L)
			{
				continue;
			}
			long num7 = (long)value3;
			if (!PlausibleObjectPointer(value3, moduleBase, moduleSize) || !Readable(handle, num7))
			{
				continue;
			}
			num3++;
			long num8 = num7 + codeCapacityAccess.CapacityOffset;
			if (!ReadU32(handle, num8, out var value4, out error) || (value4 != 0 && value4 != 16))
			{
				continue;
			}
			num4++;
			if (!dictionary.TryGetValue(num8, out var value5))
			{
				if (!globalXrefs.TryGetValue(codeCapacityAccess.GlobalRva, out var value6))
				{
					value6 = 0;
				}
				value5 = new LiveCapacityGroup
				{
					UltimateCareer = num5,
					Manager = num6,
					Config = num7,
					CapacityAddress = num8,
					Capacity = value4,
					ManagerOffset = codeCapacityAccess.ManagerOffset,
					ConfigOffset = codeCapacityAccess.ConfigOffset,
					CapacityOffset = codeCapacityAccess.CapacityOffset,
					BestGlobalRva = codeCapacityAccess.GlobalRva,
					GlobalXrefs = value6,
					EvidenceCount = 0,
					Score = 0
				};
				dictionary.Add(num8, value5);
			}
			value5.EvidenceCount++;
			if (globalXrefs.TryGetValue(codeCapacityAccess.GlobalRva, out var value7) && value7 > value5.GlobalXrefs)
			{
				value5.GlobalXrefs = value7;
				value5.BestGlobalRva = codeCapacityAccess.GlobalRva;
			}
			if (value5.CodeRvas.Count < 8)
			{
				value5.CodeRvas.Add(codeCapacityAccess.CapacityLoadRva);
			}
		}
		List<LiveCapacityGroup> list = new List<LiveCapacityGroup>(dictionary.Values);
		for (int j = 0; j < list.Count; j++)
		{
			LiveCapacityGroup liveCapacityGroup = list[j];
			int num9 = liveCapacityGroup.EvidenceCount * 100 + Math.Min(liveCapacityGroup.GlobalXrefs, 25) * 4;
			long num10 = 259972648 + StringRelocationDeltaHint;
			if (Math.Min(Math.Abs(liveCapacityGroup.BestGlobalRva - 259972648), Math.Abs(liveCapacityGroup.BestGlobalRva - num10)) <= 1048576)
			{
				num9 += 25;
			}
			if (liveCapacityGroup.ManagerOffset == 1128)
			{
				num9 += 8;
			}
			if (liveCapacityGroup.ConfigOffset == 488)
			{
				num9 += 8;
			}
			if (liveCapacityGroup.CapacityOffset == 76)
			{
				num9 += 8;
			}
			if (LooksLikeModuleVtableObject(handle, liveCapacityGroup.Config, moduleBase, moduleSize))
			{
				num9 += 5;
			}
			liveCapacityGroup.Score = num9;
		}
		list.Sort((LiveCapacityGroup a, LiveCapacityGroup b) => b.Score.CompareTo(a.Score));
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.Append("liveRoots=").Append(num.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" liveManagers=").Append(num2.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" liveConfigs=").Append(num3.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" liveZeroOr16Capacities=").Append(num4.ToString(CultureInfo.InvariantCulture));
		stringBuilder.Append(" distinctCapacityAddresses=").Append(list.Count.ToString(CultureInfo.InvariantCulture));
		for (int num11 = 0; num11 < list.Count && num11 < 10; num11++)
		{
			LiveCapacityGroup liveCapacityGroup2 = list[num11];
			stringBuilder.Append("; [").Append(num11.ToString(CultureInfo.InvariantCulture)).Append("] score=")
				.Append(liveCapacityGroup2.Score.ToString(CultureInfo.InvariantCulture))
				.Append(" evidence=")
				.Append(liveCapacityGroup2.EvidenceCount.ToString(CultureInfo.InvariantCulture))
				.Append(" globalXrefs=")
				.Append(liveCapacityGroup2.GlobalXrefs.ToString(CultureInfo.InvariantCulture))
				.Append(" global=0x")
				.Append(liveCapacityGroup2.BestGlobalRva.ToString("X", CultureInfo.InvariantCulture))
				.Append(" uc=")
				.Append(H(liveCapacityGroup2.UltimateCareer))
				.Append(" mgr=")
				.Append(H(liveCapacityGroup2.Manager))
				.Append(" cfg=")
				.Append(H(liveCapacityGroup2.Config))
				.Append(" capAddr=")
				.Append(H(liveCapacityGroup2.CapacityAddress))
				.Append(" offsets=+0x")
				.Append(liveCapacityGroup2.ManagerOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append("/+0x")
				.Append(liveCapacityGroup2.ConfigOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append("/+0x")
				.Append(liveCapacityGroup2.CapacityOffset.ToString("X", CultureInfo.InvariantCulture))
				.Append(" cap=")
				.Append(liveCapacityGroup2.Capacity.ToString(CultureInfo.InvariantCulture));
		}
		diagnostic = stringBuilder.ToString();
		if (list.Count == 0)
		{
			return false;
		}
		if (list.Count > 1 && list[0].Score <= list[1].Score + 40)
		{
			return false;
		}
		winner = list[0];
		return true;
	}

	private static bool WaitForTeamBuilderContext(IntPtr handle, Process process, long moduleBase, int moduleSize, Action<string> log, out long ultimateCareer, out long manager, out long config, out uint capacity, out uint count)
	{
		ultimateCareer = (manager = (config = 0L));
		capacity = 0u;
		count = uint.MaxValue;
		Console.WriteLine("Waiting for the visible Offline Dynasty TeamBuilder 0/0 screen...");
		Console.WriteLine("v1.06 will prove the capacity field from executable code access before writing anything.");
		Dictionary<long, int> globalXrefs;
		string diagnostic;
		List<CodeCapacityAccess> list = FindCodeBackedCapacityAccesses(handle, moduleBase, moduleSize, out globalXrefs, out diagnostic);
		log?.Invoke("CAPACITY CODE MAP: " + diagnostic);
		int error;
		List<long> hits = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes("GetTeamBuilderTeamCapacity"), null, 16, out error);
		List<long> hits2 = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes("AddTeamsToImportMap"), null, 16, out error);
		log?.Invoke("Native reflection string anchors: GetTeamBuilderTeamCapacity=" + RvaList(hits) + " AddTeamsToImportMap=" + RvaList(hits2));
		if (list.Count == 0)
		{
			log?.Invoke("CAPACITY PROOF ABORT: no accessor-shaped executable path was found. No memory was modified.");
			return false;
		}
		int num = 0;
		string text = "";
		while (!PersistentCancelRequested)
		{
			try
			{
				if (process.HasExited)
				{
					return false;
				}
			}
			catch
			{
				return false;
			}
			if (TryResolveLiveCapacityFromCode(handle, moduleBase, moduleSize, list, globalXrefs, out var winner, out var diagnostic2))
			{
				ResolvedUltimateCareerGlobalRva = winner.BestGlobalRva;
				ResolvedManagerOffset = winner.ManagerOffset;
				ResolvedConfigOffset = winner.ConfigOffset;
				ResolvedCapacityOffset = winner.CapacityOffset;
				ultimateCareer = winner.UltimateCareer;
				manager = winner.Manager;
				config = winner.Config;
				capacity = winner.Capacity;
				if (!ReadU32(handle, ultimateCareer + 2008, out count, out error))
				{
					count = uint.MaxValue;
				}
				if (log != null)
				{
					log("CAPACITY ACCESSOR PROOF: " + diagnostic2);
					log("CAPACITY ACCESSOR SELECTED: globalRVA=0x" + winner.BestGlobalRva.ToString("X", CultureInfo.InvariantCulture) + " managerOffset=+0x" + winner.ManagerOffset.ToString("X", CultureInfo.InvariantCulture) + " configOffset=+0x" + winner.ConfigOffset.ToString("X", CultureInfo.InvariantCulture) + " capacityOffset=+0x" + winner.CapacityOffset.ToString("X", CultureInfo.InvariantCulture) + " capacityAddress=" + H(winner.CapacityAddress) + " codeEvidence=" + winner.EvidenceCount.ToString(CultureInfo.InvariantCulture) + " capacityLoadRVAs=" + RvaList(winner.CodeRvas));
				}
				return true;
			}
			if (diagnostic2 != text && num % 4 == 0)
			{
				text = diagnostic2;
				log?.Invoke("CAPACITY ACCESSOR WAIT: " + diagnostic2);
			}
			num++;
			Thread.Sleep(500);
		}
		return false;
	}

	internal static int RunEmbeddedSession_v106_unused()
	{
		PersistentCancelRequested = false;
		Console.WriteLine("===============================================================================");
		Console.WriteLine("CFB27 TeamBuilder Offline v1.06 - CODE-BACKED CAPACITY PROOF");
		Console.WriteLine("===============================================================================");
		Console.WriteLine();
		Console.WriteLine("Dedicated post-patch capacity proof. This build does NOT arm the full import runtime.");
		Console.WriteLine("v1.06 derives the current UltimateCareer -> manager -> config -> capacity offsets from executable memory-access instructions.");
		Console.WriteLine("Cloud / Online Dynasty must remain OFF.");
		Console.WriteLine("This build stays active until CollegeFB27.exe exits so the temporary capacity proof can be observed and restored.");
		Console.WriteLine();
		Console.WriteLine("THIS DIAGNOSTIC MODIFIES ONLY:");
		Console.WriteLine("  1. one code-proven native TeamBuilder capacity field 0 -> 16");
		Console.WriteLine("  2. nothing else");
		Console.WriteLine("  The Blaze/owner/ProtoHttp hooks are validated read-only but NOT installed.");
		Console.WriteLine("");
		Console.WriteLine();
		Console.WriteLine("No MCR/auth/service hooks are installed in v1.06.");
		Console.WriteLine("Press Ctrl+C only if you need to stop the helper while the game is still running.");
		Console.WriteLine();
		Process process = WaitForCollegeFootballProcess();
		if (process == null)
		{
			return 2;
		}
		long num;
		int moduleMemorySize;
		string fileName;
		try
		{
			num = process.MainModule.BaseAddress.ToInt64();
			moduleMemorySize = process.MainModule.ModuleMemorySize;
			fileName = process.MainModule.FileName;
		}
		catch (Exception ex)
		{
			Console.WriteLine("ERROR reading main module: " + ex.Message);
			return 3;
		}
		ActiveModuleSize = moduleMemorySize;
		IntPtr intPtr = OpenProcess(1080u, inheritHandle: false, process.Id);
		if (intPtr == IntPtr.Zero)
		{
			Console.WriteLine("ERROR OpenProcess: " + Marshal.GetLastWin32Error().ToString(CultureInfo.InvariantCulture));
			return 4;
		}
		string path = "teambuilder-offline-capacity-proof-v1.06-results";
		string text = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Output", path);
		if (Directory.Exists(text))
		{
			Directory.Delete(text, recursive: true);
		}
		Directory.CreateDirectory(text);
		string path2 = Path.Combine(text, "v1.06-capacity-proof-log.txt");
		LocalMcrService localMcrService = null;
		HookSpec hookSpec = null;
		HookSpec hookSpec2 = null;
		HookSpec hookSpec3 = null;
		uint value = 0u;
		long num2 = 0L;
		int result = 0;
		try
		{
			StreamWriter log = new StreamWriter(path2, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			try
			{
				log.AutoFlush = true;
				Action<string> action = delegate(string text2)
				{
					Console.WriteLine(text2);
					log.WriteLine(text2);
					Action<string> externalLog = ExternalLog;
					if (externalLog != null)
					{
						try
						{
							externalLog(text2);
						}
						catch
						{
						}
					}
				};
				action("Timestamp: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
				action("PID: " + process.Id.ToString(CultureInfo.InvariantCulture));
				action("Executable: " + fileName);
				action("RuntimeBase: " + H(num));
				action("ModuleSize: " + H(moduleMemorySize));
				action("Synthetic Blaze key: " + HU(1229782938247303441uL));
				action("Synthetic AccountId fallback: DISABLED");
				long resolvedRva;
				string note;
				int error;
				bool flag = ResolveStaticString(intPtr, num, moduleMemorySize, 186259584L, "MCR_CATEGORY_ONLINELEAGUE_TB_FILE", out resolvedRva, out note, out error);
				long resolvedRva2;
				string note2;
				bool flag2 = ResolveStaticString(intPtr, num, moduleMemorySize, 186259624L, "MCR_CATEGORY_OFFLINELEAGUE_TB_FILE", out resolvedRva2, out note2, out error);
				bool flag3 = false;
				long num3 = 0L;
				if (flag && flag2)
				{
					long num4 = resolvedRva - 186259584;
					long num5 = resolvedRva2 - 186259624;
					if (num4 == num5)
					{
						flag3 = true;
						num3 = num4;
					}
				}
				long resolvedRva3;
				string note3;
				bool flag4 = ResolveStaticString(intPtr, num, moduleMemorySize, 186259792L, "%s/api/", out resolvedRva3, out note3, out error);
				if (!flag4 && flag3)
				{
					byte[] bytes = Encoding.ASCII.GetBytes("%s/api/");
					List<long> list = FindPatternRvasInModule(intPtr, num, moduleMemorySize, bytes, null, 16, out error);
					long num6 = 186259792 + num3;
					int num7 = 0;
					for (int num8 = 0; num8 < list.Count; num8++)
					{
						if (list[num8] == num6)
						{
							num7++;
						}
					}
					if (num7 == 1)
					{
						resolvedRva3 = num6;
						flag4 = true;
						note3 = "ambiguous exact string resolved by shared TeamBuilder static-data delta " + num3.ToString("+#;-#;0", CultureInfo.InvariantCulture) + "; selected 0x" + resolvedRva3.ToString("X", CultureInfo.InvariantCulture) + " from hits " + RvaList(list);
					}
				}
				action("Post-patch static string map:");
				action("  ONLINE_TB_CATEGORY old=0x" + 186259584L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag ? ("0x" + resolvedRva.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note + ")");
				action("  OFFLINE_TB_CATEGORY old=0x" + 186259624L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag2 ? ("0x" + resolvedRva2.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note2 + ")");
				action("  MCR_URL_FORMAT old=0x" + 186259792L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag4 ? ("0x" + resolvedRva3.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note3 + ")");
				if (!flag || !flag2 || !flag4)
				{
					action("ABORT: TeamBuilder/MCR identity strings could not be safely relocated in this CollegeFB27.exe build.");
					return 20;
				}
				long num9 = resolvedRva - 186259584;
				long num10 = resolvedRva2 - 186259624;
				long num11 = resolvedRva3 - 186259792;
				if (num9 == num10)
				{
					StringRelocationDeltaHint = num9;
					string[] array = new string[5]
					{
						"  Shared TeamBuilder static-data relocation delta hint: ",
						num9.ToString("+#;-#;0", CultureInfo.InvariantCulture),
						" (0x",
						null,
						null
					};
					ulong num12 = (ulong)num9;
					array[3] = num12.ToString("X", CultureInfo.InvariantCulture);
					array[4] = ")";
					action(string.Concat(array));
					if (num11 != num9)
					{
						action("  NOTE: URL format literal does not share that delta; category-string delta retained as the stronger context hint.");
					}
				}
				else
				{
					StringRelocationDeltaHint = 0L;
					action("  TeamBuilder category strings did not share one relocation delta; context locator will use structural scanning.");
				}
				hookSpec = new HookSpec("KEY_GATE_RVA_38FBA61", 59751009L, "48 85 F6 0F 84 D0 01 00 00 44 8B 8C 24 E0 00 00 00", "key");
				hookSpec2 = new HookSpec("OWNER_GATE3_RESULT_RVA_30DD944", 51239236L, "85 C0 0F 84 FD 00 00 00 80 7F 08 00 0F 84 F3 00 00 00 48 8B 7F 10 48 85 FF 0F 84 E6 00 00 00", "gate3");
				hookSpec3 = new HookSpec("PROTOHTTP_LOCAL_URL_SHIM_RVA_16D6DB0", 23948720L, "48 89 5C 24 08 57 48 83 EC 30 48 8B F9 4D 85 C0", "protohttplocalurl");
				HookSpec[] array2 = new HookSpec[3] { hookSpec, hookSpec2, hookSpec3 };
				action("Validating the three functional native hook signatures...");
				for (int num13 = 0; num13 < array2.Length; num13++)
				{
					if (!ResolveHookTarget(intPtr, num, array2[num13], out error))
					{
						action("FAIL signature: " + array2[num13].Name + " code=" + error.ToString(CultureInfo.InvariantCulture));
						action("  " + array2[num13].ValidationNote);
						return 11;
					}
					if (array2[num13].Kind == "key")
					{
						ResolvedKeyGateRva = array2[num13].ResolvedRva;
					}
					action("PASS signature: " + array2[num13].Name + " resolvedRVA=0x" + array2[num13].ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
					action("  " + array2[num13].ValidationNote);
				}
				if (!WaitForTeamBuilderContext(intPtr, process, num, moduleMemorySize, action, out var ultimateCareer, out var manager, out var config, out var capacity, out var count))
				{
					bool flag5 = false;
					try
					{
						flag5 = !process.HasExited;
					}
					catch
					{
						flag5 = false;
					}
					action("Game exited, shutdown was requested, or no unique code-backed capacity accessor became live. No capacity write was made.");
					if (flag5 && !PersistentCancelRequested)
					{
						action("CAPACITY PROOF CLOSED: executable-code proof was insufficient in this build. No memory was modified.");
						return 21;
					}
					return 0;
				}
				value = capacity;
				num2 = config + ResolvedCapacityOffset;
				action("TeamBuilder context ready: UltimateCareer=" + H(ultimateCareer) + " manager=" + H(manager) + " config=" + H(config));
				action("UltimateCareer global resolvedRVA=0x" + ResolvedUltimateCareerGlobalRva.ToString("X", CultureInfo.InvariantCulture) + " delta=" + (ResolvedUltimateCareerGlobalRva - 259972648).ToString("+#;-#;0", CultureInfo.InvariantCulture));
				action("TeamBuilder context offsets: UltimateCareer+0x" + ResolvedManagerOffset.ToString("X", CultureInfo.InvariantCulture) + " -> manager; manager+0x" + ResolvedConfigOffset.ToString("X", CultureInfo.InvariantCulture) + " -> config; config+0x" + ResolvedCapacityOffset.ToString("X", CultureInfo.InvariantCulture) + " -> capacity");
				action("Native capacity before patch: " + capacity.ToString(CultureInfo.InvariantCulture));
				action("Legacy batch-count probe [UltimateCareer+0x7D8]: " + ((count == uint.MaxValue) ? "<unreadable>" : count.ToString(CultureInfo.InvariantCulture)) + " (diagnostic only; no longer used to gate context readiness)");
				if (capacity == 0 && !WriteU32(intPtr, num2, 16u, out error))
				{
					action("FAIL setting the code-proven TeamBuilder capacity field to 16. Win32=" + error.ToString(CultureInfo.InvariantCulture));
					result = 13;
					return result;
				}
				if (!ReadU32(intPtr, num2, out var value2, out error) || value2 != 16)
				{
					action("FAIL verifying TeamBuilder capacity 16 at the code-proven field.");
					result = 14;
					return result;
				}
				action("");
				action("===============================================================================");
				action("CAPACITY PROOF PATCH ACTIVE - EXPECT THE VISIBLE PICKER TO SHOW 0/16");
				action("===============================================================================");
				action("Only the code-proven native capacity field was changed. The Blaze key, owner gate,");
				action("ProtoHttp redirect, and localhost MCR service are NOT active in this diagnostic.");
				action("If the TeamBuilder picker changes from 0/0 to 0/16, this capacity chain is proven.");
				action("Do not advance an import batch with this diagnostic. Close the game after observing the counter.");
				action("");
				while (!PersistentCancelRequested)
				{
					try
					{
						if (process.HasExited)
						{
							break;
						}
					}
					catch
					{
						break;
					}
					if (!ReadU32(intPtr, num2, out var value3, out error))
					{
						action("CAPACITY PROOF WARNING: code-proven capacity address became unreadable.");
						break;
					}
					if (value3 != 16)
					{
						action("CAPACITY PROOF WARNING: the code-proven field changed externally from 16 to " + value3.ToString(CultureInfo.InvariantCulture) + ". The diagnostic will NOT fight the game; stopping the proof loop.");
						break;
					}
					Thread.Sleep(250);
				}
				if (PersistentCancelRequested)
				{
					action("Capacity-proof shutdown requested.");
				}
				else
				{
					bool flag6 = false;
					try
					{
						flag6 = process.HasExited;
					}
					catch
					{
						flag6 = true;
					}
					if (flag6)
					{
						action("CollegeFB27.exe exited. Ending capacity-proof session.");
					}
					else
					{
						action("Capacity-proof observation loop ended while the game is still running. Temporary capacity will be restored before exit.");
					}
				}
				if (localMcrService != null)
				{
					StopLoopbackServer(localMcrService);
					WriteLoopbackServerSummary(text, localMcrService);
					action("Session totals: persisted artifact groups (NOT game import confirmations)=" + localMcrService.CompletedImports.ToString(CultureInfo.InvariantCulture) + " HTTP requests=" + localMcrService.RequestCount.ToString(CultureInfo.InvariantCulture) + " guard failure=" + ((localMcrService.GuardFailure.Length == 0) ? "<none>" : localMcrService.GuardFailure));
				}
			}
			finally
			{
				if (log != null)
				{
					((IDisposable)log).Dispose();
				}
			}
		}
		catch (Exception ex2)
		{
			try
			{
				File.AppendAllText(path2, string.Concat("FATAL: ", ex2, Environment.NewLine));
			}
			catch
			{
			}
			Console.WriteLine("FATAL: " + ex2.Message);
			result = 99;
		}
		finally
		{
			if (localMcrService != null)
			{
				try
				{
					StopLoopbackServer(localMcrService);
				}
				catch
				{
				}
			}
			bool flag7 = false;
			try
			{
				flag7 = !process.HasExited;
			}
			catch
			{
				flag7 = false;
			}
			if (flag7)
			{
				RestoreHook(intPtr, hookSpec3, out var error2);
				RestoreHook(intPtr, hookSpec2, out error2);
				RestoreHook(intPtr, hookSpec, out error2);
				if (num2 != 0L)
				{
					WriteU32(intPtr, num2, value, out error2);
				}
			}
			CloseHandle(intPtr);
		}
		return result;
	}

	private static string HexBytes(byte[] bytes, int max)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return "<none>";
		}
		int num = Math.Min(bytes.Length, max);
		StringBuilder stringBuilder = new StringBuilder(num * 3);
		for (int i = 0; i < num; i++)
		{
			if (i != 0)
			{
				stringBuilder.Append(' ');
			}
			stringBuilder.Append(bytes[i].ToString("X2", CultureInfo.InvariantCulture));
		}
		if (bytes.Length > num)
		{
			stringBuilder.Append(" ...");
		}
		return stringBuilder.ToString();
	}

	private static bool IsExecutableAddress(IntPtr handle, long address)
	{
		if (Query(handle, address, out var mbi) && mbi.State == 4096)
		{
			return IsExecutableProtection(mbi.Protect);
		}
		return false;
	}

	private static bool TryDecodeRipDataReference(byte[] code, int offset, long instructionVa, out long targetVa, out int length, out string kind)
	{
		targetVa = 0L;
		length = 0;
		kind = "";
		if (code == null || offset < 0 || offset >= code.Length)
		{
			return false;
		}
		int num = offset;
		byte b = 0;
		if (code[num] >= 64 && code[num] <= 79)
		{
			b = code[num++];
			if (num >= code.Length)
			{
				return false;
			}
		}
		byte b2 = code[num++];
		if (b2 != 141 && b2 != 139)
		{
			return false;
		}
		if (num >= code.Length)
		{
			return false;
		}
		byte num2 = code[num++];
		int num3 = (num2 >> 6) & 3;
		int num4 = num2 & 7;
		if (num3 != 0 || num4 != 5)
		{
			return false;
		}
		if (num + 4 > code.Length)
		{
			return false;
		}
		int num5 = BitConverter.ToInt32(code, num);
		num += 4;
		length = num - offset;
		targetVa = instructionVa + length + num5;
		kind = ((b2 == 141) ? "LEA" : "MOV") + (((b & 8) != 0) ? "64" : "");
		return true;
	}

	private static List<long> FindExecutableRipRefsToTarget(IntPtr handle, long moduleBase, int moduleSize, long targetVa, int maxHits)
	{
		List<long> list = new List<long>();
		long num = moduleBase + moduleSize;
		long num2 = moduleBase;
		int error = 0;
		MBI64 mbi;
		while (num2 < num && list.Count < maxHits && Query(handle, num2, out mbi))
		{
			long baseAddress = (long)mbi.BaseAddress;
			long num3;
			try
			{
				num3 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num3 = num;
			}
			if (num3 <= num2)
			{
				num3 = num2 + 4096;
			}
			long num4 = Math.Max(num2, baseAddress);
			long num5 = Math.Min(num, num3);
			if (mbi.State == 4096 && IsExecutableProtection(mbi.Protect) && num5 > num4)
			{
				int num7;
				for (long num6 = num4; num6 < num5; num6 += num7)
				{
					if (list.Count >= maxHits)
					{
						break;
					}
					num7 = (int)Math.Min(1048576L, num5 - num6);
					int length = (int)Math.Min((long)num7 + 16L, num5 - num6);
					byte[] array = Read(handle, num6, length, out error);
					if (array.Length == 0)
					{
						break;
					}
					for (int i = 0; i < num7 && i < array.Length; i++)
					{
						if (list.Count >= maxHits)
						{
							break;
						}
						if (TryDecodeRipDataReference(array, i, num6 + i, out var targetVa2, out var _, out var _) && targetVa2 == targetVa)
						{
							long item = num6 + i - moduleBase;
							if (!list.Contains(item))
							{
								list.Add(item);
							}
						}
					}
				}
			}
			num2 = Math.Max(num2 + 4096, num3);
		}
		return list;
	}

	private static List<long> FindDirectCallersOfTarget(IntPtr handle, long moduleBase, int moduleSize, long targetRva, int maxHits)
	{
		List<long> list = new List<long>();
		long num = moduleBase + moduleSize;
		long num2 = moduleBase;
		int error = 0;
		MBI64 mbi;
		while (num2 < num && list.Count < maxHits && Query(handle, num2, out mbi))
		{
			long baseAddress = (long)mbi.BaseAddress;
			long num3;
			try
			{
				num3 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num3 = num;
			}
			if (num3 <= num2)
			{
				num3 = num2 + 4096;
			}
			long num4 = Math.Max(num2, baseAddress);
			long num5 = Math.Min(num, num3);
			if (mbi.State == 4096 && IsExecutableProtection(mbi.Protect) && num5 > num4)
			{
				int num7;
				for (long num6 = num4; num6 < num5; num6 += num7)
				{
					if (list.Count >= maxHits)
					{
						break;
					}
					num7 = (int)Math.Min(1048576L, num5 - num6);
					byte[] array = Read(handle, num6, num7, out error);
					if (array.Length == 0)
					{
						break;
					}
					for (int i = 0; i + 5 <= array.Length; i++)
					{
						if (list.Count >= maxHits)
						{
							break;
						}
						if (array[i] == 232)
						{
							int num8 = BitConverter.ToInt32(array, i + 1);
							long num9 = num6 + i - moduleBase;
							if (num9 + 5 + num8 == targetRva && !list.Contains(num9))
							{
								list.Add(num9);
							}
						}
					}
				}
			}
			num2 = Math.Max(num2 + 4096, num3);
		}
		return list;
	}

	private static List<NamedTraceRef> FindRawModuleRefsToValue(IntPtr handle, long moduleBase, int moduleSize, long targetVa, long targetRva, int maxHits)
	{
		List<NamedTraceRef> list = new List<NamedTraceRef>();
		long num = moduleBase + moduleSize;
		long num2 = moduleBase;
		int error = 0;
		byte[] bytes = BitConverter.GetBytes((ulong)targetVa);
		byte[] bytes2 = BitConverter.GetBytes((uint)targetRva);
		MBI64 mbi;
		while (num2 < num && list.Count < maxHits && Query(handle, num2, out mbi))
		{
			long baseAddress = (long)mbi.BaseAddress;
			long num3;
			try
			{
				num3 = checked(baseAddress + (long)mbi.RegionSize);
			}
			catch
			{
				num3 = num;
			}
			if (num3 <= num2)
			{
				num3 = num2 + 4096;
			}
			long num4 = Math.Max(num2, baseAddress);
			long num5 = Math.Min(num, num3);
			if (mbi.State == 4096 && IsReadableProtection(mbi.Protect) && num5 > num4)
			{
				int num7;
				for (long num6 = num4; num6 < num5; num6 += num7)
				{
					if (list.Count >= maxHits)
					{
						break;
					}
					num7 = (int)Math.Min(1048576L, num5 - num6);
					int length = (int)Math.Min((long)num7 + 8L, num5 - num6);
					byte[] array = Read(handle, num6, length, out error);
					if (array.Length == 0)
					{
						break;
					}
					for (int i = 0; i < num7; i++)
					{
						if (list.Count >= maxHits)
						{
							break;
						}
						if (i + 8 <= array.Length)
						{
							bool flag = true;
							for (int j = 0; j < 8; j++)
							{
								if (array[i + j] != bytes[j])
								{
									flag = false;
									break;
								}
							}
							if (flag)
							{
								long num8 = num6 + i - moduleBase;
								if (num8 != targetRva)
								{
									list.Add(new NamedTraceRef
									{
										RefRva = num8,
										Kind = "VA64",
										Width = 8
									});
								}
							}
						}
						if (i + 4 > array.Length)
						{
							continue;
						}
						bool flag2 = true;
						for (int k = 0; k < 4; k++)
						{
							if (array[i + k] != bytes2[k])
							{
								flag2 = false;
								break;
							}
						}
						if (flag2)
						{
							long num9 = num6 + i - moduleBase;
							if (num9 != targetRva)
							{
								list.Add(new NamedTraceRef
								{
									RefRva = num9,
									Kind = "RVA32",
									Width = 4
								});
							}
						}
					}
				}
			}
			num2 = Math.Max(num2 + 4096, num3);
		}
		List<NamedTraceRef> list2 = new List<NamedTraceRef>();
		for (int l = 0; l < list.Count; l++)
		{
			bool flag3 = false;
			for (int m = 0; m < list2.Count; m++)
			{
				if (list2[m].RefRva == list[l].RefRva && list2[m].Kind == list[l].Kind)
				{
					flag3 = true;
					break;
				}
			}
			if (!flag3)
			{
				list2.Add(list[l]);
			}
		}
		return list2;
	}

	private static void AddCandidateCode(List<long> candidates, long rva, int moduleSize)
	{
		if (rva >= 0 && rva < moduleSize && !candidates.Contains(rva))
		{
			candidates.Add(rva);
		}
	}

	private static void CollectExecutablePointersNearRef(IntPtr handle, long moduleBase, int moduleSize, long refRva, List<long> candidates, Action<string> log)
	{
		long num = Math.Max(0L, refRva - 128);
		int length = (int)Math.Min(272L, moduleSize - num);
		int error;
		byte[] array = Read(handle, moduleBase + num, length, out error);
		log("    neighborhood RVA 0x" + num.ToString("X", CultureInfo.InvariantCulture) + " bytes=" + HexBytes(array, 272));
		if (array.Length < 4)
		{
			return;
		}
		for (int i = 0; i + 4 <= array.Length; i += 4)
		{
			long num2 = num + i;
			uint num3 = BitConverter.ToUInt32(array, i);
			if (num3 < (uint)moduleSize && IsExecutableAddress(handle, moduleBase + num3))
			{
				AddCandidateCode(candidates, num3, moduleSize);
				log("      EXEC RVA32 @+0x" + (num2 - refRva).ToString("X", CultureInfo.InvariantCulture) + " -> 0x" + num3.ToString("X", CultureInfo.InvariantCulture));
			}
			if (i + 8 <= array.Length && (i & 7) == 0)
			{
				long num4 = (long)BitConverter.ToUInt64(array, i);
				if (num4 >= moduleBase && num4 < moduleBase + moduleSize && IsExecutableAddress(handle, num4))
				{
					long rva = num4 - moduleBase;
					AddCandidateCode(candidates, rva, moduleSize);
					log("      EXEC VA64 @+0x" + (num2 - refRva).ToString("X", CultureInfo.InvariantCulture) + " -> 0x" + rva.ToString("X", CultureInfo.InvariantCulture));
				}
			}
		}
	}

	private static void TraceNamedTeamBuilderMethod(IntPtr handle, long moduleBase, int moduleSize, string methodName, Action<string> log)
	{
		int error;
		List<long> list = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes(methodName), null, 32, out error);
		log("");
		log("===============================================================================");
		log("NAMED METHOD TRACE: " + methodName);
		log("===============================================================================");
		log("Exact string hits: " + RvaList(list));
		if (list.Count != 1)
		{
			log("TRACE CLOSED: expected exactly one method-name string; got " + list.Count.ToString(CultureInfo.InvariantCulture));
			return;
		}
		long num = list[0];
		long num2 = moduleBase + num;
		byte[] bytes = Read(handle, Math.Max(moduleBase, num2 - 64), 192, out error);
		log("String RVA=0x" + num.ToString("X", CultureInfo.InvariantCulture) + " VA=" + H(num2));
		log("String neighborhood bytes: " + HexBytes(bytes, 192));
		List<long> list2 = FindExecutableRipRefsToTarget(handle, moduleBase, moduleSize, num2, 64);
		log("Direct executable RIP refs to method-name string: " + RvaList(list2));
		for (int i = 0; i < list2.Count; i++)
		{
			long num3 = list2[i];
			byte[] bytes2 = Read(handle, moduleBase + Math.Max(0L, num3 - 48), 144, out error);
			log("  direct-code-ref 0x" + num3.ToString("X", CultureInfo.InvariantCulture) + " around=" + HexBytes(bytes2, 144));
		}
		List<NamedTraceRef> list3 = FindRawModuleRefsToValue(handle, moduleBase, moduleSize, num2, num, 128);
		log("Raw module refs to string VA/RVA: count=" + list3.Count.ToString(CultureInfo.InvariantCulture));
		List<long> list4 = new List<long>();
		for (int j = 0; j < list3.Count; j++)
		{
			NamedTraceRef namedTraceRef = list3[j];
			log("  raw-ref[" + j.ToString(CultureInfo.InvariantCulture) + "] " + namedTraceRef.Kind + " RVA=0x" + namedTraceRef.RefRva.ToString("X", CultureInfo.InvariantCulture));
			CollectExecutablePointersNearRef(handle, moduleBase, moduleSize, namedTraceRef.RefRva, list4, log);
			List<long> list5 = FindExecutableRipRefsToTarget(handle, moduleBase, moduleSize, moduleBase + namedTraceRef.RefRva, 32);
			if (list5.Count != 0)
			{
				log("    executable RIP refs to this metadata/data ref: " + RvaList(list5));
				for (int k = 0; k < list5.Count; k++)
				{
					AddCandidateCode(list4, list5[k], moduleSize);
				}
			}
		}
		for (int l = 0; l < list2.Count; l++)
		{
			AddCandidateCode(list4, list2[l], moduleSize);
		}
		log("Candidate executable RVAs gathered from named metadata: " + RvaList(list4));
		for (int m = 0; m < list4.Count && m < 64; m++)
		{
			long num4 = list4[m];
			byte[] bytes3 = Read(handle, moduleBase + num4, 96, out error);
			List<long> hits = FindDirectCallersOfTarget(handle, moduleBase, moduleSize, num4, 32);
			log("  CODE[" + m.ToString(CultureInfo.InvariantCulture) + "] RVA=0x" + num4.ToString("X", CultureInfo.InvariantCulture) + " callers=" + RvaList(hits) + " bytes=" + HexBytes(bytes3, 96));
		}
	}

	private static bool TryResolveNamedVtableDispatch(IntPtr handle, long moduleBase, int moduleSize, string methodName, long expectedSingletonGlobal, int expectedSlot, Action<string> L, out long wrapper, out long binding, out long target, out int error)
	{
		wrapper = 0L;
		binding = 0L;
		target = 0L;
		error = 0;
		List<long> list = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes(methodName + "\0"), null, 8, out error);
		L("Named method string: " + methodName + "=" + RvaList(list));
		if (list.Count != 1)
		{
			error = 31101;
			return false;
		}
		List<NamedTraceRef> list2 = FindRawModuleRefsToValue(handle, moduleBase, moduleSize, moduleBase + list[0], list[0], 32);
		for (int i = 0; i < list2.Count; i++)
		{
			NamedTraceRef namedTraceRef = list2[i];
			if (namedTraceRef.Kind != "VA64" || namedTraceRef.RefRva < 8 || !ReadU64(handle, moduleBase + namedTraceRef.RefRva - 8, out var value, out error))
			{
				continue;
			}
			long num = (long)value;
			if (num < moduleBase || num >= moduleBase + moduleSize)
			{
				continue;
			}
			byte[] array = Read(handle, num, 64, out error);
			if (array.Length < 37 || array[30] != 76 || array[31] != 141 || array[32] != 5)
			{
				continue;
			}
			long num2 = num + 37 + BitConverter.ToInt32(array, 33);
			if (num2 < moduleBase || num2 >= moduleBase + moduleSize)
			{
				continue;
			}
			byte[] array2 = Read(handle, num2, 192, out error);
			if (array2.Length < 32)
			{
				continue;
			}
			bool flag = false;
			bool flag2 = false;
			for (int j = 0; j < array2.Length; j++)
			{
				if (TryDecodeRipDataReference(array2, j, num2 + j, out var targetVa, out var _, out var _) && targetVa == expectedSingletonGlobal)
				{
					flag = true;
				}
				if (j + 6 <= array2.Length && array2[j] == byte.MaxValue && (array2[j + 1] & 0xF8) == 144 && BitConverter.ToInt32(array2, j + 2) == expectedSlot)
				{
					flag2 = true;
				}
			}
			if (flag2 && ReadU64(handle, expectedSingletonGlobal, out var value2, out error) && value2 != 0L && ReadU64(handle, (long)value2, out var value3, out error) && value3 != 0L && ReadU64(handle, (long)value3 + (long)expectedSlot, out var value4, out error))
			{
				long num3 = (long)value4;
				if (num3 >= moduleBase && num3 < moduleBase + moduleSize && IsExecutableAddress(handle, num3))
				{
					wrapper = num;
					binding = num2;
					target = num3;
					L("PASS named dispatch: " + methodName + " wrapperRVA=0x" + (wrapper - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " bindingRVA=0x" + (binding - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " vtableSlot=+0x" + expectedSlot.ToString("X", CultureInfo.InvariantCulture) + " targetRVA=0x" + (target - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " directSingletonRef=" + flag.ToString(CultureInfo.InvariantCulture));
					return true;
				}
			}
		}
		error = 31102;
		L("FAIL named dispatch: " + methodName + " did not prove its unique named wrapper and vtable slot +0x" + expectedSlot.ToString("X", CultureInfo.InvariantCulture) + " against the live capacity-proven TeamBuilder vtable.");
		return false;
	}

	private static byte[] BuildHardReloadRelay(long state, long loadTarget, long originalReloadTarget)
	{
		List<byte> list = new List<byte>();
		Add(list, 73, 186);
		Imm64(list, (ulong)state);
		Add(list, 65, 187, 1, 0, 0, 0);
		Add(list, 69, 135, 90, 8);
		Add(list, 69, 133, 219);
		int displacementIndex = EmitRel32Placeholder(list, 15, 133);
		Add(list, 240, 73, 255, 2);
		Add(list, 72, 131, 236, 40);
		Add(list, 73, 187);
		Imm64(list, (ulong)loadTarget);
		Add(list, 65, 255, 211);
		Add(list, 72, 131, 196, 40);
		Add(list, 73, 186);
		Imm64(list, (ulong)state);
		Add(list, 65, 199, 66, 8, 0, 0, 0, 0);
		Add(list, 195);
		int count = list.Count;
		PatchRel32(list, displacementIndex, count);
		Add(list, 73, 187);
		Imm64(list, (ulong)originalReloadTarget);
		Add(list, 65, 255, 227);
		return list.ToArray();
	}

	private static bool TryInstallHardReloadOverride(IntPtr handle, long moduleBase, int moduleSize, CapacityVtablePatch capacity, Action<string> L, out ReloadVtablePatch patch, out int error)
	{
		patch = null;
		error = 0;
		if (capacity == null || capacity.SingletonGlobalRva == 0L || capacity.Vtable == 0L)
		{
			error = 31103;
			return false;
		}
		long expectedSingletonGlobal = moduleBase + capacity.SingletonGlobalRva;
		if (!TryResolveNamedVtableDispatch(handle, moduleBase, moduleSize, "LoadTeamBuilderAssetData", expectedSingletonGlobal, 2440, L, out var _, out var _, out var target, out error))
		{
			return false;
		}
		if (!TryResolveNamedVtableDispatch(handle, moduleBase, moduleSize, "ReloadTeamBuilderAssetData", expectedSingletonGlobal, 2448, L, out var _, out var _, out var target2, out error))
		{
			return false;
		}
		if (target == target2)
		{
			error = 31104;
			L("FAIL hard reload proof: Load and Reload already resolve to the same native target; no override was made.");
			return false;
		}
		long num = capacity.Vtable + 2448;
		if (!ReadU64(handle, num, out var value, out error) || value != (ulong)target2)
		{
			error = 31105;
			L("FAIL hard reload proof: Reload slot changed between named-method proof and installation.");
			return false;
		}
		IntPtr intPtr = VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)4096u, 12288u, 64u);
		if (intPtr == IntPtr.Zero)
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		long num2 = intPtr.ToInt64();
		long num3 = num2 + 256;
		byte[] array = BuildHardReloadRelay(num3, target, target2);
		if (!Write(handle, num2, array, out error) || !Write(handle, num3, new byte[16], out error))
		{
			return false;
		}
		FlushInstructionCache(handle, new IntPtr(num2), (UIntPtr)(ulong)array.Length);
		if (!WriteProtected(handle, num, BitConverter.GetBytes((ulong)num2), out error))
		{
			return false;
		}
		if (!ReadU64(handle, num, out value, out error) || value != (ulong)num2)
		{
			error = 31106;
			return false;
		}
		patch = new ReloadVtablePatch
		{
			SingletonGlobalRva = capacity.SingletonGlobalRva,
			Singleton = capacity.Singleton,
			Vtable = capacity.Vtable,
			SlotAddress = num,
			OriginalReloadTarget = target2,
			LoadTarget = target,
			StubAddress = num2,
			CounterAddress = num3
		};
		L("Hard reload override armed: ReloadTeamBuilderAssetData now enters the game's proven LoadTeamBuilderAssetData target. Existing save IDs and method arguments remain unchanged.");
		L("  reloadSlot=" + H(num) + " originalReload=" + H(target2) + " loadTarget=" + H(target) + " guardedRelay=" + H(num2));
		return true;
	}

	private static bool RestoreHardReloadOverride(IntPtr handle, ReloadVtablePatch patch, out int error)
	{
		error = 0;
		if (patch == null || patch.SlotAddress == 0L || patch.OriginalReloadTarget == 0L)
		{
			return true;
		}
		if (!ReadU64(handle, patch.SlotAddress, out var value, out error))
		{
			return false;
		}
		if (value == (ulong)patch.OriginalReloadTarget)
		{
			return true;
		}
		if (value != (ulong)patch.StubAddress)
		{
			error = 31107;
			B("RESTORE SKIPPED: ReloadTeamBuilderAssetData slot is no longer owned by this plugin.");
			return false;
		}
		return WriteProtected(handle, patch.SlotAddress, BitConverter.GetBytes((ulong)patch.OriginalReloadTarget), out error);
	}

	private static string HardReloadWatchdogTick(IntPtr handle, long moduleBase, int moduleSize, ReloadVtablePatch patch, Action<string> L)
	{
		if (patch == null)
		{
			return "NO_PATCH";
		}
		long address = moduleBase + patch.SingletonGlobalRva;
		if (!ReadU64(handle, address, out var value, out var error) || value == 0L || !ReadU64(handle, (long)value, out var value2, out error) || value2 == 0L)
		{
			return "SINGLETON_UNAVAILABLE Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		long num = (long)value2;
		if (num < moduleBase || num >= moduleBase + moduleSize)
		{
			return "VTABLE_OUTSIDE_MODULE " + H(num);
		}
		if (!ReadU64(handle, num + 2440, out var value3, out error) || !ReadU64(handle, num + 2448, out var value4, out error))
		{
			return "SLOT_READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		if (value3 != (ulong)patch.LoadTarget)
		{
			return "UNKNOWN_LOAD_TARGET " + H((long)value3);
		}
		patch.Singleton = (long)value;
		patch.Vtable = num;
		patch.SlotAddress = num + 2448;
		if (value4 == (ulong)patch.StubAddress)
		{
			return "ARMED";
		}
		if (value4 != (ulong)patch.OriginalReloadTarget)
		{
			return "UNKNOWN_RELOAD_TARGET " + H((long)value4);
		}
		if (!WriteProtected(handle, patch.SlotAddress, BitConverter.GetBytes((ulong)patch.StubAddress), out error))
		{
			return "REARM_WRITE_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		L("HARD RELOAD WATCHDOG: TeamBuilder vtable returned to the exact original Reload target; guarded Load relay safely re-armed.");
		return "REARMED";
	}

	private static byte[] BuildSavedAssetKeyFallbackRelay(long status, long originalAssetKeyTarget)
	{
		return OfflineTeamBuilderRuntime.GameProfile.Current.Emit("MainAssetKey", unchecked((ulong)status), unchecked((ulong)originalAssetKeyTarget));
	}

	private static byte[] BuildStockLifecycleCareerRelay(long status, long originalCareerTarget, long assetKeyRelay, long loadTarget, long reloadTarget)
	{
		return OfflineTeamBuilderRuntime.GameProfile.Current.Emit("MainCareer", unchecked((ulong)status), unchecked((ulong)originalCareerTarget), unchecked((ulong)assetKeyRelay), unchecked((ulong)loadTarget), unchecked((ulong)reloadTarget));
	}

	private static bool VerifyTargetPrefix(IntPtr handle, long target, byte[] expected, string name, Action<string> L, out int error)
	{
		byte[] array = Read(handle, target, expected.Length, out error);
		if (array.Length == expected.Length && ExactMatches(array, 0, expected))
		{
			return true;
		}
		error = 31204;
		L("FAIL stock-lifecycle proof: " + name + " entry bytes differ from the live-captured current build.");
		return false;
	}

	private static bool TryInstallDurablePairRepair(IntPtr handle, long moduleBase, int moduleSize, CapacityVtablePatch capacity, Action<string> L, out DurablePairRepairPatch patch, out int error)
	{
		patch = null;
		error = 0;
		if (capacity == null || !BuildCompatibility.MayValidateModule(moduleSize))
		{
			error = 31201;
			L("FAIL stock-lifecycle proof: this repair is defined only for the current 0x11ECF000 game image.");
			return false;
		}
		long num = moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceVtable");
		if (capacity.Vtable != num)
		{
			error = 31202;
			L("FAIL stock-lifecycle proof: capacity-proven TeamBuilder vtable did not match current build RVA 0x" + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceVtable").ToString("X", CultureInfo.InvariantCulture) + ".");
			return false;
		}
		long num2 = capacity.Vtable + 1240;
		long num3 = capacity.Vtable + 2160;
		long address = capacity.Vtable + 2440;
		long address2 = capacity.Vtable + 2448;
		long address3 = capacity.Vtable + 2456;
		if (!ReadU64(handle, num2, out var value, out error) || !ReadU64(handle, num3, out var value2, out error) || !ReadU64(handle, address, out var value3, out error) || !ReadU64(handle, address2, out var value4, out error) || !ReadU64(handle, address3, out var value5, out error))
		{
			L("FAIL stock-lifecycle proof: required TeamBuilder vtable slots could not be read.");
			return false;
		}
		long num4 = (long)value;
		long num5 = (long)value2;
		long num6 = (long)value3;
		long num7 = (long)value4;
		long num8 = (long)value5;
		if (num4 != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("CareerHub") || num5 != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("AssetKey") || num6 != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Load") || num7 != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Reload") || num8 != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("Store"))
		{
			error = 31203;
			L("FAIL stock-lifecycle proof: one or more current-build method targets changed. Nothing was patched.");
			L("  AssetKey=" + H(num5) + " Load=" + H(num6) + " Reload=" + H(num7) + " Store=" + H(num8) + " CareerHub=" + H(num4));
			return false;
		}
		if (!IsExecutableAddress(handle, num5) || !IsExecutableAddress(handle, num6) || !IsExecutableAddress(handle, num7) || !IsExecutableAddress(handle, num8) || !IsExecutableAddress(handle, num4))
		{
			error = 31204;
			L("FAIL stock-lifecycle proof: a required stock target is not executable.");
			return false;
		}
		if (!VerifyTargetPrefix(handle, num5, new byte[16]
		{
			72, 139, 129, 112, 4, 0, 0, 72, 133, 192,
			116, 20, 72, 139, 128, 208
		}, "AssetKey", L, out error) || !VerifyTargetPrefix(handle, num6, new byte[16]
		{
			72, 139, 137, 104, 4, 0, 0, 72, 133, 201,
			116, 12, 72, 139, 137, 232
		}, "Load", L, out error) || !VerifyTargetPrefix(handle, num7, new byte[9] { 64, 87, 72, 131, 236, 48, 72, 139, 249 }, "Reload", L, out error) || !VerifyTargetPrefix(handle, num4, new byte[16]
		{
			72, 131, 236, 56, 128, 185, 180, 7, 0, 0,
			0, 198, 129, 177, 7, 0
		}, "CareerHub", L, out error))
		{
			return false;
		}
		IntPtr intPtr = VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)4096u, 12288u, 64u);
		if (intPtr == IntPtr.Zero)
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		long num9 = intPtr.ToInt64();
		long num10 = num9 + 1024;
		long num11 = num9 + 3072;
		byte[] array = BuildSavedAssetKeyFallbackRelay(num11, num5);
		byte[] array2 = BuildStockLifecycleCareerRelay(num11, num4, num9, num6, num7);
		if (array.Length >= 1024 || array2.Length >= 2048 || !Write(handle, num9, array, out error) || !Write(handle, num10, array2, out error) || !Write(handle, num11, new byte[256], out error))
		{
			L("FAIL stock-lifecycle repair: remote relay/status write failed.");
			return false;
		}
		FlushInstructionCache(handle, new IntPtr(num9), (UIntPtr)3072u);
		if (!WriteProtected(handle, num3, BitConverter.GetBytes((ulong)num9), out error) || !ReadU64(handle, num3, out var value6, out error) || value6 != (ulong)num9)
		{
			L("FAIL stock-lifecycle repair: AssetKey slot did not accept the RTG-safe fallback relay.");
			return false;
		}
		if (!WriteProtected(handle, num2, BitConverter.GetBytes((ulong)num10), out error) || !ReadU64(handle, num2, out var value7, out error) || value7 != (ulong)num10)
		{
			WriteProtected(handle, num3, BitConverter.GetBytes((ulong)num5), out var _);
			L("FAIL stock-lifecycle repair: CareerHub slot did not accept the lifecycle relay; AssetKey was restored.");
			return false;
		}
		patch = new DurablePairRepairPatch
		{
			SingletonGlobalRva = capacity.SingletonGlobalRva,
			Vtable = capacity.Vtable,
			CareerSlotAddress = num2,
			AssetKeySlotAddress = num3,
			OriginalCareerTarget = num4,
			OriginalAssetKeyTarget = num5,
			LoadTarget = num6,
			ReloadTarget = num7,
			CareerStubAddress = num10,
			AssetKeyStubAddress = num9,
			StatusAddress = num11
		};
		L("Saved-career stock asset lifecycle armed on OnCareerHubEntered plus RTG-safe AssetKey fallback.");
		L("  currentBuild vtableRVA=0x" + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceVtable").ToString("X", CultureInfo.InvariantCulture) + " careerSlot=+0x4D8 assetKeySlot=+0x870.");
		L("  sequence: stock CareerHub -> validated saved slot -> stock Load(slot) -> stock Reload/Store validation -> stock CareerHub continuation.");
		return true;
	}

	private static bool RestoreDurablePairRepair(IntPtr handle, DurablePairRepairPatch patch, out int error)
	{
		error = 0;
		if (patch == null)
		{
			return true;
		}
		bool flag = true;
		if (ReadU64(handle, patch.CareerSlotAddress, out var value, out error) && value == (ulong)patch.CareerStubAddress)
		{
			flag = WriteProtected(handle, patch.CareerSlotAddress, BitConverter.GetBytes((ulong)patch.OriginalCareerTarget), out error) && flag;
		}
		else if (value != (ulong)patch.OriginalCareerTarget)
		{
			error = 31205;
			B("RESTORE SKIPPED: CareerHub slot is no longer owned by this plugin.");
			flag = false;
		}
		if (ReadU64(handle, patch.AssetKeySlotAddress, out var value2, out error) && value2 == (ulong)patch.AssetKeyStubAddress)
		{
			flag = WriteProtected(handle, patch.AssetKeySlotAddress, BitConverter.GetBytes((ulong)patch.OriginalAssetKeyTarget), out error) && flag;
		}
		else if (value2 != (ulong)patch.OriginalAssetKeyTarget)
		{
			error = 31206;
			B("RESTORE SKIPPED: AssetKey slot is no longer owned by this plugin.");
			flag = false;
		}
		return flag;
	}

	private static string DurablePairWatchdogTick(IntPtr handle, long moduleBase, int moduleSize, DurablePairRepairPatch patch, Action<string> L)
	{
		if (patch == null)
		{
			return "NO_PATCH";
		}
		long address = moduleBase + patch.SingletonGlobalRva;
		if (!ReadU64(handle, address, out var value, out var error) || value == 0L || !ReadU64(handle, (long)value, out var value2, out error) || value2 == 0L)
		{
			return "SINGLETON_UNAVAILABLE Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		long num = (long)value2;
		if (num != moduleBase + OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceVtable"))
		{
			return "UNKNOWN_VTABLE " + H(num);
		}
		patch.Vtable = num;
		patch.CareerSlotAddress = num + 1240;
		patch.AssetKeySlotAddress = num + 2160;
		if (!ReadU64(handle, patch.AssetKeySlotAddress, out var value3, out error) || !ReadU64(handle, patch.CareerSlotAddress, out var value4, out error))
		{
			return "SLOT_READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		bool flag = false;
		if (value3 == (ulong)patch.OriginalAssetKeyTarget)
		{
			if (!WriteProtected(handle, patch.AssetKeySlotAddress, BitConverter.GetBytes((ulong)patch.AssetKeyStubAddress), out error))
			{
				return "ASSETKEY_REARM_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
			}
			flag = true;
		}
		else if (value3 != (ulong)patch.AssetKeyStubAddress)
		{
			return "UNKNOWN_ASSETKEY_TARGET " + H((long)value3);
		}
		if (value4 == (ulong)patch.OriginalCareerTarget)
		{
			if (!WriteProtected(handle, patch.CareerSlotAddress, BitConverter.GetBytes((ulong)patch.CareerStubAddress), out error))
			{
				return "CAREER_REARM_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
			}
			flag = true;
		}
		else if (value4 != (ulong)patch.CareerStubAddress)
		{
			// The unified asset runtime intentionally chains this exact relay.
			// Accept only its registered address AND byte-for-byte emitted code,
			// never an arbitrary external detour with a similar-looking prefix.
			if (IsOwnedAssetChain == null || !IsOwnedAssetChain(handle, value4, (ulong)patch.CareerStubAddress))
				return "UNKNOWN_CAREER_TARGET " + H((long)value4);
		}
		if (ReadU64(handle, (long)(value + 1128), out var value5, out error) && value5 == 0L && ReadU64(handle, patch.StatusAddress + 16, out var value6, out error) && value6 != 0L)
		{
			Write(handle, patch.StatusAddress + 8, new byte[4], out error);
			Write(handle, patch.StatusAddress + 16, new byte[8], out error);
			Write(handle, patch.StatusAddress + 24, new byte[4], out error);
			L("STOCK LIFECYCLE: career context closed; plugin one-shot guard reset for the next save entry.");
		}
		if (flag)
		{
			L("STOCK LIFECYCLE WATCHDOG: exact original vtable target returned; validated relay safely re-armed.");
		}
		if (!flag)
		{
			return "ARMED";
		}
		return "REARMED";
	}

	private static string DurablePairSkipText(uint code)
	{
		return code switch
		{
			0u => "stock Load/Reload validation completed", 
			1u => "stock ready gate handled without repair", 
			2u => "saved asset context not available yet", 
			3u => "no validated replacement slot", 
			4u => "this career context/slot was already attempted", 
			5u => "lifecycle relay reentry guard active", 
			6u => "stock Reload declined asset validation", 
			_ => "unknown", 
		};
	}

	private static bool TryInstallCapacityVtableOverride(IntPtr handle, Process process, long moduleBase, int moduleSize, Action<string> L, out CapacityVtablePatch patch, out int error)
	{
		patch = null;
		error = 0;
		List<long> list = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes("GetTeamBuilderTeamCapacity\0"), null, 8, out error);
		List<long> list2 = FindPatternRvasInModule(handle, moduleBase, moduleSize, Encoding.ASCII.GetBytes("AddTeamsToImportMap\0"), null, 8, out error);
		L("Named method strings: GetTeamBuilderTeamCapacity=" + RvaList(list) + " AddTeamsToImportMap=" + RvaList(list2));
		if (list.Count != 1 || list2.Count != 1)
		{
			L("FAIL capacity proof: expected one exact string hit for each TeamBuilder method.");
			error = 30977;
			return false;
		}
		List<NamedTraceRef> list3 = FindRawModuleRefsToValue(handle, moduleBase, moduleSize, moduleBase + list[0], list[0], 16);
		List<NamedTraceRef> list4 = FindRawModuleRefsToValue(handle, moduleBase, moduleSize, moduleBase + list2[0], list2[0], 16);
		long num = -1L;
		long num2 = -1L;
		for (int i = 0; i < list3.Count; i++)
		{
			if (list3[i].Kind == "VA64")
			{
				num = list3[i].RefRva;
				break;
			}
		}
		for (int j = 0; j < list4.Count; j++)
		{
			if (list4[j].Kind == "VA64")
			{
				num2 = list4[j].RefRva;
				break;
			}
		}
		if (num < 8 || num2 < 8)
		{
			L("FAIL capacity proof: named-method metadata entry could not be resolved.");
			error = 30978;
			return false;
		}
		if (!ReadU64(handle, moduleBase + num - 8, out var value, out error) || !ReadU64(handle, moduleBase + num2 - 8, out var value2, out error))
		{
			L("FAIL capacity proof: could not read named-method wrapper pointers. Win32=" + error.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		long num3 = (long)value;
		long num4 = (long)value2;
		long num5 = num3 - moduleBase;
		long num6 = num4 - moduleBase;
		L("Named metadata wrappers: capacity=0x" + num5.ToString("X", CultureInfo.InvariantCulture) + " add=0x" + num6.ToString("X", CultureInfo.InvariantCulture));
		if (num5 < 0 || num5 >= moduleSize || num6 < 0 || num6 >= moduleSize)
		{
			L("FAIL capacity proof: metadata wrapper pointer is outside CollegeFB27.exe.");
			error = 30979;
			return false;
		}
		byte[] array = Read(handle, num3, 52, out error);
		byte[] array2 = Read(handle, num4, 52, out error);
		if (array.Length < 37 || array2.Length < 37 || array[30] != 76 || array[31] != 141 || array[32] != 5 || array2[30] != 76 || array2[31] != 141 || array2[32] != 5)
		{
			L("FAIL capacity proof: named-method wrapper no longer has the proven LEA r8 native-binding shape.");
			error = 30980;
			return false;
		}
		long num7 = num3 + 37 + BitConverter.ToInt32(array, 33);
		long num8 = num4 + 37 + BitConverter.ToInt32(array2, 33);
		L("Native binding targets: GetTeamBuilderTeamCapacity=0x" + (num7 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " AddTeamsToImportMap=0x" + (num8 - moduleBase).ToString("X", CultureInfo.InvariantCulture));
		byte[] array3 = Read(handle, num7, 64, out error);
		byte[] array4 = Read(handle, num8, 96, out error);
		if (array3.Length < 35 || array3[19] != 72 || array3[20] != 139 || array3[21] != 13 || array3[26] != 72 || array3[27] != 139 || array3[28] != 1 || array3[29] != byte.MaxValue || array3[30] != 144 || BitConverter.ToInt32(array3, 31) != 1016)
		{
			L("FAIL capacity proof: GetTeamBuilderTeamCapacity no longer dispatches through vtable +0x3F8.");
			error = 30981;
			return false;
		}
		if (array4.Length < 90 || array4[68] != 72 || array4[69] != 139 || array4[70] != 5 || array4[80] != 72 || array4[81] != 139 || array4[82] != 16 || array4[83] != 72 || array4[84] != 139 || array4[85] != 170 || BitConverter.ToInt32(array4, 86) != 1032)
		{
			L("FAIL capacity proof: AddTeamsToImportMap no longer has the proven shared-global / vtable+0x408 shape.");
			error = 30982;
			return false;
		}
		long num9 = num7 + 26 + BitConverter.ToInt32(array3, 22);
		if (num8 + 75 + BitConverter.ToInt32(array4, 71) != num9)
		{
			L("FAIL capacity proof: GetCapacity and AddTeams did not resolve to the same TeamBuilder native singleton global.");
			error = 30983;
			return false;
		}
		long num10 = num9 - moduleBase;
		L("Shared TeamBuilder native singleton global RVA=0x" + num10.ToString("X", CultureInfo.InvariantCulture));
		if (!BuildCompatibility.TryGetExpectedSingletonRva(moduleSize, out var rva))
		{
			L("FAIL capacity proof: no singleton profile is defined for module size 0x" + moduleSize.ToString("X", CultureInfo.InvariantCulture) + ".");
			error = 30988;
			return false;
		}
		L("Capacity profile: " + BuildCompatibility.Describe(moduleSize));
		if (!BuildCompatibility.MatchesSingletonProfile(moduleSize, num10))
		{
			L("FAIL capacity proof: current-build singleton RVA mismatch. Expected 0x" + rva.ToString("X", CultureInfo.InvariantCulture) + " but resolved 0x" + num10.ToString("X", CultureInfo.InvariantCulture) + ".");
			error = 30988;
			return false;
		}
		L("PASS capacity singleton profile: both native bindings resolve to the expected 0x" + num10.ToString("X", CultureInfo.InvariantCulture) + ". Instance, vtable, accessor, and write/readback checks follow.");
		ulong value3 = 0uL;
		ulong value4 = 0uL;
		bool flag = false;
		bool flag2 = false;
		while (!PersistentCancelRequested)
		{
			try
			{
				if (process.HasExited)
				{
					break;
				}
			}
			catch
			{
				break;
			}
			if (ReadU64(handle, num9, out value3, out error) && value3 != 0L && ReadU64(handle, (long)value3, out value4, out error) && value4 != 0L)
			{
				long num11 = (long)value4;
				if (num11 >= moduleBase && num11 < moduleBase + moduleSize)
				{
					flag = true;
					break;
				}
			}
			if (!flag2)
			{
				flag2 = true;
				L("Waiting for TeamBuilder native singleton/vtable initialization (no timeout while CollegeFB27.exe remains alive)...");
				B("STATE WAIT_FOR_TB_SINGLETON sharedGlobalRVA=0x" + num10.ToString("X", CultureInfo.InvariantCulture));
			}
			Thread.Sleep(500);
		}
		if (!flag)
		{
			L("FAIL capacity proof: TeamBuilder native singleton/vtable did not become readable before the selected game process ended or shutdown was requested.");
			error = 30984;
			return false;
		}
		long num12 = (long)value3;
		long num13 = (long)value4;
		if (!ReadU64(handle, num13 + 1016, out var value5, out error) || !ReadU64(handle, num13 + 1032, out var value6, out error))
		{
			L("FAIL capacity proof: could not read TeamBuilder capacity/AddTeams virtual slots.");
			return false;
		}
		long num14 = (long)value5;
		long num15 = (long)value6;
		if (num14 < moduleBase || num14 >= moduleBase + moduleSize || num15 < moduleBase || num15 >= moduleBase + moduleSize || num14 == num15)
		{
			L("FAIL capacity proof: capacity/AddTeams vtable targets are not distinct executable addresses inside CollegeFB27.exe.");
			error = 30985;
			return false;
		}
		L("Capacity virtual targets: getterRva=0x" + (num14 - moduleBase).ToString("X", CultureInfo.InvariantCulture) + " addTeamsRva=0x" + (num15 - moduleBase).ToString("X", CultureInfo.InvariantCulture));
		byte[] array5 = Read(handle, num14, 18, out error);
		byte[] array6 = Hex("48 8B 81 68 04 00 00 48 8B 80 E8 01 00 00 8B 40 4C C3");
		L("Capacity accessor bytes: " + BytesHex(array5));
		if (array5.Length != array6.Length || !ExactMatches(array5, 0, array6))
		{
			L("FAIL capacity proof: native capacity virtual body no longer matches the v1.08-proven +0x468/+0x1E8/+0x4C accessor. actual=[" + BytesHex(array5) + "]");
			error = 30986;
			return false;
		}
		byte[] array7 = new byte[6] { 184, 16, 0, 0, 0, 195 };
		IntPtr intPtr = VirtualAllocEx(handle, IntPtr.Zero, (UIntPtr)4096u, 12288u, 64u);
		if (intPtr == IntPtr.Zero)
		{
			error = Marshal.GetLastWin32Error();
			L("FAIL capacity proof: VirtualAllocEx for return-16 stub failed. Win32=" + error.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		long num16 = intPtr.ToInt64();
		if (!Write(handle, num16, array7, out error))
		{
			L("FAIL capacity proof: writing return-16 stub failed. Win32=" + error.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		FlushInstructionCache(handle, new IntPtr(num16), (UIntPtr)(ulong)array7.Length);
		long num17 = num13 + 1016;
		if (!VirtualProtectEx(handle, new IntPtr(num17), (UIntPtr)8u, 64u, out var oldProtect))
		{
			error = Marshal.GetLastWin32Error();
			L("FAIL capacity proof: VirtualProtectEx on capacity vtable slot failed. Win32=" + error.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		bool num18 = Write(handle, num17, BitConverter.GetBytes((ulong)num16), out error);
		VirtualProtectEx(handle, new IntPtr(num17), (UIntPtr)8u, oldProtect, out var _);
		FlushInstructionCache(handle, new IntPtr(num17), (UIntPtr)8u);
		if (!num18)
		{
			L("FAIL capacity proof: capacity vtable pointer write failed. Win32=" + error.ToString(CultureInfo.InvariantCulture));
			return false;
		}
		if (!ReadU64(handle, num17, out var value7, out error) || value7 != (ulong)num16)
		{
			L("FAIL capacity proof: capacity vtable slot readback did not match the return-16 stub.");
			error = 30987;
			return false;
		}
		patch = new CapacityVtablePatch();
		patch.SingletonGlobalRva = num10;
		patch.Singleton = num12;
		patch.Vtable = num13;
		patch.SlotAddress = num17;
		patch.OriginalTarget = num14;
		patch.StubAddress = num16;
		patch.AddTeamsTarget = num15;
		L("Capacity override armed: singleton=" + H(num12) + " vtable=" + H(num13) + " slot=" + H(num17) + " originalTarget=" + H(num14) + " return16Stub=" + H(num16));
		return true;
	}

	private static bool RestoreCapacityVtableOverride(IntPtr handle, CapacityVtablePatch patch, out int error)
	{
		error = 0;
		if (patch == null || patch.SlotAddress == 0L || patch.OriginalTarget == 0L)
		{
			return true;
		}
		if (!ReadU64(handle, patch.SlotAddress, out var value, out error))
		{
			return false;
		}
		if (value == (ulong)patch.OriginalTarget)
		{
			return true;
		}
		if (value != (ulong)patch.StubAddress)
		{
			error = 31002;
			B("RESTORE SKIPPED: capacity slot is no longer owned by this plugin.");
			return false;
		}
		if (!VirtualProtectEx(handle, new IntPtr(patch.SlotAddress), (UIntPtr)8u, 64u, out var oldProtect))
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		bool result = Write(handle, patch.SlotAddress, BitConverter.GetBytes((ulong)patch.OriginalTarget), out error);
		VirtualProtectEx(handle, new IntPtr(patch.SlotAddress), (UIntPtr)8u, oldProtect, out var _);
		FlushInstructionCache(handle, new IntPtr(patch.SlotAddress), (UIntPtr)8u);
		return result;
	}

	private static bool WriteProtected(IntPtr handle, long address, byte[] bytes, out int error)
	{
		error = 0;
		if (bytes == null || bytes.Length == 0)
		{
			return true;
		}
		if (!VirtualProtectEx(handle, new IntPtr(address), (UIntPtr)(ulong)bytes.Length, 64u, out var oldProtect))
		{
			error = Marshal.GetLastWin32Error();
			return false;
		}
		bool result = Write(handle, address, bytes, out error);
		FlushInstructionCache(handle, new IntPtr(address), (UIntPtr)(ulong)bytes.Length);
		VirtualProtectEx(handle, new IntPtr(address), (UIntPtr)(ulong)bytes.Length, oldProtect, out var _);
		return result;
	}

	private static string CapacityWatchdogTick(IntPtr handle, long moduleBase, int moduleSize, CapacityVtablePatch patch, Action<string> L)
	{
		if (patch == null)
		{
			return "NO_PATCH";
		}
		long address = moduleBase + patch.SingletonGlobalRva;
		if (!ReadU64(handle, address, out var value, out var error))
		{
			return "SINGLETON_READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		if (value == 0L)
		{
			return "SINGLETON_NULL";
		}
		long num = (long)value;
		if (!ReadU64(handle, num, out var value2, out error) || value2 == 0L)
		{
			return "VTABLE_READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		long num2 = (long)value2;
		if (num2 < moduleBase || num2 >= moduleBase + moduleSize)
		{
			return "VTABLE_OUTSIDE_MODULE " + H(num2);
		}
		if (!ReadU64(handle, num2 + 1016, out var value3, out error) || !ReadU64(handle, num2 + 1032, out var value4, out error))
		{
			return "SLOT_READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		long num3 = (long)value3;
		long num4 = (long)value4;
		if (num4 != patch.AddTeamsTarget)
		{
			return "UNKNOWN_ADDTEAMS_TARGET " + H(num4) + " expected=" + H(patch.AddTeamsTarget);
		}
		long num5 = num2 + 1016;
		patch.Singleton = num;
		patch.Vtable = num2;
		patch.SlotAddress = num5;
		if (num3 == patch.StubAddress)
		{
			return "ARMED";
		}
		if (num3 != patch.OriginalTarget)
		{
			return "UNKNOWN_CAPACITY_TARGET " + H(num3) + " expectedOriginal=" + H(patch.OriginalTarget) + " expectedStub=" + H(patch.StubAddress);
		}
		if (!WriteProtected(handle, num5, BitConverter.GetBytes((ulong)patch.StubAddress), out error))
		{
			return "REARM_WRITE_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		if (!ReadU64(handle, num5, out var value5, out error) || value5 != (ulong)patch.StubAddress)
		{
			return "REARM_VERIFY_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		L("CAPACITY WATCHDOG: TeamBuilder singleton/vtable returned to the exact proven original capacity target; safely re-armed return-16 override.");
		B("WATCHDOG CAPACITY_REARM singleton=" + H(num) + " vtable=" + H(num2) + " slot=" + H(num5));
		return "REARMED";
	}

	private static byte[] InstalledHookPatch(HookSpec spec)
	{
		if (spec == null || spec.PatchLen < 14 || spec.Remote == 0L)
		{
			return new byte[0];
		}
		List<byte> list = new List<byte>();
		Add(list, 255, 37, 0, 0, 0, 0);
		Imm64(list, (ulong)spec.Remote);
		while (list.Count < spec.PatchLen)
		{
			list.Add(144);
		}
		return list.ToArray();
	}

	private static string HookWatchdogTick(IntPtr handle, HookSpec spec, Action<string> L)
	{
		if (spec == null || spec.ResolvedTarget == 0L || spec.Original == null || spec.Original.Length == 0)
		{
			return "NOT_INSTALLED";
		}
		int error;
		byte[] array = Read(handle, spec.ResolvedTarget, spec.PatchLen, out error);
		if (array.Length != spec.PatchLen)
		{
			return "READ_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		byte[] array2 = InstalledHookPatch(spec);
		if (array2.Length == spec.PatchLen && ExactMatches(array, 0, array2))
		{
			return "ARMED";
		}
		if (!ExactMatches(array, 0, spec.Original))
		{
			return "UNKNOWN_BYTES actual=[" + BytesHex(array) + "]";
		}
		if (!WriteProtected(handle, spec.ResolvedTarget, array2, out error))
		{
			return "REARM_WRITE_FAIL Win32=" + error.ToString(CultureInfo.InvariantCulture);
		}
		L("HOOK WATCHDOG: " + spec.Name + " returned to its exact captured original bytes; safely re-installed existing shim jump.");
		B("WATCHDOG HOOK_REARM " + spec.Name + " target=" + H(spec.ResolvedTarget));
		return "REARMED";
	}

	internal static int RunEmbeddedSession(Process process, string outputRoot)
	{
		PersistentCancelRequested = false;
		LastSessionReachedReady = false;
		Console.WriteLine("===============================================================================");
		Console.WriteLine(OfflineTeamBuilderRuntime.PluginIdentity.DisplayName + " " + OfflineTeamBuilderRuntime.PluginIdentity.Version + " - import/local asset service");
		Console.WriteLine("===============================================================================");
		Console.WriteLine();
		Console.WriteLine("OFFLINE ONLY. Cloud/Online Dynasty must remain OFF.");
		Console.WriteLine("Native profile: " + OfflineTeamBuilderRuntime.GameProfile.Current.id + ". Live signatures, instance/vtable/accessor guards required; no executable checksum requirement.");
		Console.WriteLine();
		if (process == null)
		{
			B("ABORT: RunEmbeddedSession received no selected CollegeFB27 process.");
			return 2;
		}
		if (string.IsNullOrEmpty(outputRoot))
		{
			B("ABORT: authoritative Mod Manager Output root was not supplied.");
			return 5;
		}
		long num;
		int moduleMemorySize;
		string fileName;
		try
		{
			num = process.MainModule.BaseAddress.ToInt64();
			moduleMemorySize = process.MainModule.ModuleMemorySize;
			fileName = process.MainModule.FileName;
		}
		catch (Exception ex)
		{
			B("ATTACH failure reading selected PID " + process.Id.ToString(CultureInfo.InvariantCulture) + " MainModule: " + ex.Message);
			Console.WriteLine("ERROR reading main module: " + ex.Message);
			return 3;
		}
		ActiveModuleSize = moduleMemorySize;
		B("STATE RESOLVE_PID_STATIC PID=" + process.Id.ToString(CultureInfo.InvariantCulture) + " executable=" + fileName + " moduleBase=" + H(num) + " moduleSize=" + H(moduleMemorySize));
		string text = "<unavailable>";
		try
		{
			FileInfo fileInfo = new FileInfo(fileName);
			FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(fileName);
			text = "fileLength=" + fileInfo.Length.ToString(CultureInfo.InvariantCulture) + " lastWrite=" + fileInfo.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture) + " fileVersion=" + versionInfo.FileVersion + " productVersion=" + versionInfo.ProductVersion;
		}
		catch (Exception ex2)
		{
			text = "identity read failed: " + ex2.Message;
		}
		B("CollegeFB27 executable identity: " + text);
		if (!BuildCompatibility.MayValidateModule(moduleMemorySize))
		{
			B("ABORT IMAGE_BOUNDS: selected CollegeFB27 image 0x" + moduleMemorySize.ToString("X", CultureInfo.InvariantCulture) + " cannot safely contain this profile's native targets; not an exact-size allow-list.");
			Console.WriteLine("ERROR: native profile targets exceed safe loaded-image bounds. No native hooks will be installed.");
			return 30;
		}
		B("CANDIDATE_IMAGE_ACCEPTED " + BuildCompatibility.Describe(moduleMemorySize) + "; signature, singleton, accessor and vtable validation still required.");
		B("Compatibility uses live native signatures and vtable contracts. Executable file checksums are not read or required.");
		IntPtr intPtr = OpenProcess(1080u, inheritHandle: false, process.Id);
		if (intPtr == IntPtr.Zero)
		{
			int lastWin32Error = Marshal.GetLastWin32Error();
			B("ATTACH OpenProcess FAILED PID=" + process.Id.ToString(CultureInfo.InvariantCulture) + " Win32=" + lastWin32Error.ToString(CultureInfo.InvariantCulture));
			Console.WriteLine("ERROR OpenProcess: " + lastWin32Error.ToString(CultureInfo.InvariantCulture));
			return 4;
		}
		B("ATTACH OpenProcess PASS PID=" + process.Id.ToString(CultureInfo.InvariantCulture));
		string sessionName = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + "_PID" + process.Id.ToString(CultureInfo.InvariantCulture);
		string text2 = Path.Combine(outputRoot, sessionName);
		string path = Path.Combine(text2, "import-session.log");
		try
		{
			Directory.CreateDirectory(text2);
		}
		catch (Exception ex4)
		{
			B("ABORT: could not create session Output directory " + text2 + ": " + ex4.Message);
			CloseHandle(intPtr);
			return 6;
		}
		B("Session Output location: " + text2);
		LocalMcrService localMcrService = null;
		HookSpec hookSpec = null;
		HookSpec hookSpec2 = null;
		HookSpec hookSpec3 = null;
		CapacityVtablePatch patch = null;
		ReloadVtablePatch reloadVtablePatch = null;
		DurablePairRepairPatch patch2 = null;
		int num2 = 0;
		try
		{
			StreamWriter log = new StreamWriter(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			try
			{
				log.AutoFlush = true;
				Action<string> action = delegate(string textLine)
				{
					Console.WriteLine(textLine);
					log.WriteLine(textLine);
					B("NATIVE " + textLine);
					Action<string> externalLog = ExternalLog;
					if (externalLog != null)
					{
						try
						{
							externalLog(textLine);
						}
						catch
						{
						}
					}
				};
				action("Timestamp: " + DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
				action("PID: " + process.Id.ToString(CultureInfo.InvariantCulture));
				action("Executable: " + fileName);
				action("RuntimeBase: " + H(num));
				action("ModuleSize: " + H(moduleMemorySize));
				action("Synthetic Blaze key: " + HU(1229782938247303441uL));
				action("Synthetic AccountId fallback: DISABLED");
				action("Mode: " + OfflineTeamBuilderRuntime.PluginIdentity.Version + "; stock slot-aware saved-asset Load/Reload/Career lifecycle plus validated local asset revisions");
				long resolvedRva;
				string note;
				int error;
				bool flag = ResolveStaticString(intPtr, num, moduleMemorySize, 186259584L, "MCR_CATEGORY_ONLINELEAGUE_TB_FILE", out resolvedRva, out note, out error);
				long resolvedRva2;
				string note2;
				bool flag2 = ResolveStaticString(intPtr, num, moduleMemorySize, 186259624L, "MCR_CATEGORY_OFFLINELEAGUE_TB_FILE", out resolvedRva2, out note2, out error);
				bool flag3 = false;
				long num3 = 0L;
				if (flag && flag2)
				{
					long num4 = resolvedRva - 186259584;
					long num5 = resolvedRva2 - 186259624;
					if (num4 == num5)
					{
						flag3 = true;
						num3 = num4;
					}
				}
				long resolvedRva3;
				string note3;
				bool flag4 = ResolveStaticString(intPtr, num, moduleMemorySize, 186259792L, "%s/api/", out resolvedRva3, out note3, out error);
				if (!flag4 && flag3)
				{
					List<long> list = FindPatternRvasInModule(intPtr, num, moduleMemorySize, Encoding.ASCII.GetBytes("%s/api/"), null, 16, out error);
					long num6 = 186259792 + num3;
					int num7 = 0;
					for (int num8 = 0; num8 < list.Count; num8++)
					{
						if (list[num8] == num6)
						{
							num7++;
						}
					}
					if (num7 == 1)
					{
						resolvedRva3 = num6;
						flag4 = true;
						note3 = "ambiguous exact string resolved by shared TeamBuilder static-data delta " + num3.ToString("+#;-#;0", CultureInfo.InvariantCulture) + "; selected 0x" + resolvedRva3.ToString("X", CultureInfo.InvariantCulture) + " from hits " + RvaList(list);
					}
				}
				action("Post-patch static string map:");
				action("  ONLINE_TB_CATEGORY old=0x" + 186259584L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag ? ("0x" + resolvedRva.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note + ")");
				action("  OFFLINE_TB_CATEGORY old=0x" + 186259624L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag2 ? ("0x" + resolvedRva2.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note2 + ")");
				action("  MCR_URL_FORMAT old=0x" + 186259792L.ToString("X", CultureInfo.InvariantCulture) + " new=" + (flag4 ? ("0x" + resolvedRva3.ToString("X", CultureInfo.InvariantCulture)) : "<not unique>") + " (" + note3 + ")");
				if (!flag || !flag2 || !flag4)
				{
					action("ABORT: TeamBuilder/MCR identity strings could not be safely relocated.");
					return 20;
				}
				long num9 = resolvedRva - 186259584;
				long num10 = resolvedRva2 - 186259624;
				StringRelocationDeltaHint = ((num9 == num10) ? num9 : 0);
				action("Shared TeamBuilder static-data relocation delta hint: " + ((StringRelocationDeltaHint == 0L) ? "<none>" : StringRelocationDeltaHint.ToString("+#;-#;0", CultureInfo.InvariantCulture)));
				hookSpec = new HookSpec("KEY_GATE_RVA_38FBA61", 59751009L, "48 85 F6 0F 84 D0 01 00 00 44 8B 8C 24 E0 00 00 00", "key");
				hookSpec2 = new HookSpec("OWNER_GATE3_RESULT_RVA_30DD944", 51239236L, "85 C0 0F 84 FD 00 00 00 80 7F 08 00 0F 84 F3 00 00 00 48 8B 7F 10 48 85 FF 0F 84 E6 00 00 00", "gate3");
				hookSpec3 = new HookSpec("PROTOHTTP_LOCAL_URL_SHIM_RVA_16D6DB0", 23948720L, "48 89 5C 24 08 57 48 83 EC 30 48 8B F9 4D 85 C0", "protohttplocalurl");
				HookSpec[] array = new HookSpec[3] { hookSpec, hookSpec2, hookSpec3 };
				action("Validating current-build native hook signatures...");
				for (int num11 = 0; num11 < array.Length; num11++)
				{
					if (!ResolveHookTarget(intPtr, num, array[num11], out error))
					{
						action("FAIL signature: " + array[num11].Name + " code=" + error.ToString(CultureInfo.InvariantCulture));
						action("  " + array[num11].ValidationNote);
						return 11;
					}
					if (array[num11].Kind == "key")
					{
						ResolvedKeyGateRva = array[num11].ResolvedRva;
					}
					action("PASS signature: " + array[num11].Name + " resolvedRVA=0x" + array[num11].ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
					action("  " + array[num11].ValidationNote);
				}
				B("STATE START_MCR");
				localMcrService = StartLoopbackServer(text2, out var errorText);
				if (localMcrService == null)
				{
					action("FAIL starting localhost MCR service: " + errorText);
					return 15;
				}
				LocalLoopbackBaseUrl = "http://127.0.0.1:" + localMcrService.Port.ToString(CultureInfo.InvariantCulture);
				action("Local MCR service listening at " + LocalLoopbackBaseUrl + ".");
				B("STATE WAIT_FOR_TB_SINGLETON / ARM_CAPACITY");
				if (!TryInstallCapacityVtableOverride(intPtr, process, num, moduleMemorySize, action, out patch, out error))
				{
					action("ABORT: v1.08-proven TeamBuilder capacity override could not be installed. code=" + error.ToString(CultureInfo.InvariantCulture));
					return 21;
				}
				B("STATE ARM_CAPACITY PASS singleton=" + H(patch.Singleton) + " vtable=" + H(patch.Vtable));
				B("STATE ARM_STOCK_SAVED_ASSET_LIFECYCLE");
				if (!TryInstallDurablePairRepair(intPtr, num, moduleMemorySize, patch, action, out patch2, out error))
				{
					action("ABORT: current-build stock saved-asset lifecycle could not be proven. code=" + error.ToString(CultureInfo.InvariantCulture) + ". No unsafe Load/Reload substitution was attempted.");
					B("STATE ARM_STOCK_SAVED_ASSET_LIFECYCLE FAILED code=" + error.ToString(CultureInfo.InvariantCulture));
					return 23;
				}
				B("STATE ARM_STOCK_SAVED_ASSET_LIFECYCLE PASS careerSlot=" + H(patch2.CareerSlotAddress) + " assetKeySlot=" + H(patch2.AssetKeySlotAddress));
				B("STATE ARM_FUNCTIONAL_HOOKS");
				for (int num12 = 0; num12 < array.Length; num12++)
				{
					if (!InstallHook(intPtr, num, array[num12], mode2: false, out error))
					{
						action("FAIL installing functional hook: " + array[num12].Name + " code=" + error.ToString(CultureInfo.InvariantCulture));
						num2 = 12;
						return num2;
					}
					action("Installed: " + array[num12].Name + " at RVA 0x" + array[num12].ResolvedRva.ToString("X", CultureInfo.InvariantCulture));
				}
				if (!ReadU64(intPtr, patch.SlotAddress, out var value, out error) || value != (ulong)patch.StubAddress)
				{
					action("FAIL: capacity vtable override was lost before runtime READY.");
					return 22;
				}
				action("");
				action("===============================================================================");
				action("STOCK SAVED-ASSET LIFECYCLE READY - DYNASTY AND RTG RE-ENTRY ARMED");
				LastSessionReachedReady = true;
				int error2 = process.Id;
				B("STATE READY PID=" + error2.ToString(CultureInfo.InvariantCulture));
				B("STATE WATCHDOG capacity=1s hooks=5s no recurring module scans");
				action("===============================================================================");
				action("Capacity: named GetTeamBuilderTeamCapacity vtable slot returns 16.");
				action("Career reload: stock CareerHub initializes the save, a validated replacement slot is resolved (including RTG saved-context fallback), then stock Load(slot) and stock Reload/Store validate the render assets before stock CareerHub continues.");
				action("Blaze key: current-build scoped key shim installed.");
				action("Owner/readiness: current-build Gate3 compatibility shim installed.");
				action("ProtoHttp: current-build /api/ requests redirect to localhost MCR service.");
				action("TEST ONLY ON A NEW DISPOSABLE CAREER. Wait for transport readiness, import ONE team, inspect every visual, then test a cold restart. Stored uploads are not proof of native rendering.");
				action("");
				int num13 = -1;
				int num14 = -1;
				string text3 = "";
				string text4 = "";
				string text5 = ((reloadVtablePatch == null) ? "UNAVAILABLE" : "");
				string text6 = ((patch2 == null) ? "UNAVAILABLE" : "");
				string[] array2 = new string[array.Length];
				DateTime dateTime = DateTime.UtcNow;
				DateTime dateTime2 = DateTime.UtcNow;
				DateTime dateTime3 = DateTime.MinValue;
				bool? flag5 = null;
				ulong num15 = ulong.MaxValue;
				ulong num16 = ulong.MaxValue;
				ulong num17 = ulong.MaxValue;
				ulong num18 = ulong.MaxValue;
				uint num19 = uint.MaxValue;
				uint num20 = uint.MaxValue;
				DateTime dateTime4 = DateTime.MinValue;
				bool flag6 = false;
				try
				{
					long num21 = hookSpec3.ResolvedTarget - 96;
					int error3;
					byte[] array3 = ((num21 >= num && num21 + 768 <= num + moduleMemorySize) ? Read(intPtr, num21, 768, out error3) : new byte[0]);
					if (num21 >= num && num21 + 768 <= num + moduleMemorySize && array3.Length == 768 && hookSpec3.Original != null)
					{
						Buffer.BlockCopy(hookSpec3.Original, 0, array3, 96, hookSpec3.Original.Length);
						File.WriteAllBytes(Path.Combine(text2, "native-request-code.bin"), array3);
						RepairJson.AtomicWrite(Path.Combine(text2, "native-request-code-info.json"), RepairJson.Encode(new Dictionary<string, string>
						{
							{
								"start_rva",
								"0x" + (num21 - num).ToString("X", CultureInfo.InvariantCulture)
							},
							{
								"selected_entry_rva",
								"0x" + hookSpec3.ResolvedRva.ToString("X", CultureInfo.InvariantCulture)
							},
							{ "note", "Read-only code snapshot; original entry bytes restored IN THIS FILE ONLY for disassembly. No extra native hook installed." }
						}));
					}
				}
				catch (Exception ex5)
				{
					B("DIAGNOSTIC_CODE_READ_WARNING " + ex5.Message);
				}
				while (!PersistentCancelRequested)
				{
					try
					{
						if (process.HasExited)
						{
							break;
						}
					}
					catch
					{
						break;
					}
					DateTime utcNow = DateTime.UtcNow;
					try
					{
						ulong num22 = DataU64(intPtr, hookSpec3, 0);
						ulong num23 = DataU64(intPtr, hookSpec, 0);
						ulong num24 = DataU64(intPtr, hookSpec2, 0);
						ulong value2 = 0uL;
						uint value3 = 0u;
						uint value4 = 0u;
						uint value5 = 0u;
						if (patch2 != null)
						{
							ReadU32(intPtr, patch2.StatusAddress, out value3, out error2);
							ReadU32(intPtr, patch2.StatusAddress + 4, out value5, out error2);
							ReadU32(intPtr, patch2.StatusAddress + 36, out value4, out error2);
							value2 = value4;
						}
						else if (reloadVtablePatch != null)
						{
							ReadU64(intPtr, reloadVtablePatch.CounterAddress, out value2, out error2);
						}
						bool flag7 = value3 != num19 || value4 != num20;
						bool flag8 = num22 != num15 || num23 != num16 || num24 != num17 || value2 != num18 || flag7;
						if (patch2 != null && flag7 && value3 != 0)
						{
							ReadU32(intPtr, patch2.StatusAddress + 24, out var value6, out error2);
							ReadU32(intPtr, patch2.StatusAddress + 12, out var value7, out error2);
							ReadU32(intPtr, patch2.StatusAddress + 28, out var value8, out error2);
							ReadU32(intPtr, patch2.StatusAddress + 32, out var value9, out error2);
							action("STOCK LIFECYCLE CALLBACK #" + value3.ToString(CultureInfo.InvariantCulture) + ": " + DurablePairSkipText(value5) + "; successes=" + value4.ToString(CultureInfo.InvariantCulture) + " slot=0x" + value6.ToString("X", CultureInfo.InvariantCulture) + " fallbackKeys=" + value7.ToString(CultureInfo.InvariantCulture) + " Load=" + value8.ToString(CultureInfo.InvariantCulture) + " Reload=" + value9.ToString(CultureInfo.InvariantCulture));
						}
						if (flag8 || utcNow >= dateTime4)
						{
							string safeUrlSample = "";
							string urlSampleStatus = "not sampled";
							if (num22 != num15 && num22 != 0L)
							{
								ulong num25 = DataU64(intPtr, hookSpec3, 8);
								int error4 = 0;
								if (num25 >= 65536 && num25 <= long.MaxValue && Readable(intPtr, (long)num25))
								{
									safeUrlSample = ReloadDiagnostics.SafeUrl(ReadAsciiZ(intPtr, (long)num25, 2048, out error4));
									urlSampleStatus = "last-pointer sample; may miss bursts/expired pointers; Win32=" + error4.ToString(CultureInfo.InvariantCulture);
								}
								else
								{
									urlSampleStatus = "last URL pointer unavailable";
								}
							}
							ReloadDiagnostics.Append(text2, new ReloadProbeRecord
							{
								Event = (flag8 ? "NATIVE_COUNTER_CHANGE" : "HEARTBEAT"),
								NativeRequestCalls = num22,
								RelativeApiRedirects = DataU64(intPtr, hookSpec3, 16),
								HardReimportCalls = value2,
								KeyGateCalls = num23,
								OwnerGateCalls = num24,
								OwnerGateFailureCode = DataU64(intPtr, hookSpec2, 56),
								LocalHttpRequests = localMcrService.RequestCount,
								StoredGroups = localMcrService.CompletedImports,
								ServiceAlive = localMcrService.IsHealthy,
								CapacityState = text4,
								SafeUrlSample = safeUrlSample,
								UrlSampleStatus = urlSampleStatus
							});
							num15 = num22;
							num16 = num23;
							num17 = num24;
							num18 = value2;
							num19 = value3;
							num20 = value4;
							dateTime4 = utcNow.AddSeconds(10.0);
						}
					}
					catch (Exception ex6)
					{
						if (!flag6)
						{
							B("RELOAD_TRACE_WRITE_WARNING " + ex6.Message);
							flag6 = true;
						}
					}
					if (utcNow >= dateTime)
					{
						dateTime = utcNow.AddSeconds(1.0);
						string text7 = CapacityWatchdogTick(intPtr, num, moduleMemorySize, patch, action);
						if (text7 == "REARMED")
						{
							text7 = "ARMED";
						}
						if (text7 != text4)
						{
							if (text7 != "ARMED")
							{
								action("CAPACITY WATCHDOG STATE: " + text7 + ". Unknown states are not overwritten.");
								B("WATCHDOG CAPACITY_STATE " + text7);
							}
							else if (text4.Length != 0 && text4 != "ARMED")
							{
								action("CAPACITY WATCHDOG STATE: ARMED.");
								B("WATCHDOG CAPACITY_STATE ARMED");
							}
							text4 = text7;
						}
						if (reloadVtablePatch != null)
						{
							string text8 = HardReloadWatchdogTick(intPtr, num, moduleMemorySize, reloadVtablePatch, action);
							if (text8 == "REARMED")
							{
								text8 = "ARMED";
							}
							if (text8 != text5)
							{
								if (text8 != "ARMED")
								{
									action("HARD RELOAD WATCHDOG STATE: " + text8 + ". Unknown states are not overwritten.");
								}
								text5 = text8;
							}
						}
						if (patch2 != null)
						{
							string text9 = DurablePairWatchdogTick(intPtr, num, moduleMemorySize, patch2, action);
							if (text9 == "REARMED")
							{
								text9 = "ARMED";
							}
							if (text9 != text6)
							{
								if (text9 != "ARMED")
								{
									action("STOCK LIFECYCLE WATCHDOG STATE: " + text9 + ". Unknown targets are not overwritten.");
								}
								text6 = text9;
							}
						}
					}
					if (utcNow >= dateTime2)
					{
						dateTime2 = utcNow.AddSeconds(5.0);
						for (int num26 = 0; num26 < array.Length; num26++)
						{
							string text10 = HookWatchdogTick(intPtr, array[num26], action);
							if (text10 == "REARMED")
							{
								text10 = "ARMED";
							}
							string text11 = array2[num26] ?? "";
							if (text10 != text11)
							{
								if (text10 != "ARMED")
								{
									action("HOOK WATCHDOG STATE: " + array[num26].Name + " -> " + text10 + ". Unknown bytes are not overwritten.");
									B("WATCHDOG HOOK_STATE " + array[num26].Name + " " + text10);
								}
								else if (text11.Length != 0 && text11 != "ARMED")
								{
									action("HOOK WATCHDOG STATE: " + array[num26].Name + " -> ARMED.");
									B("WATCHDOG HOOK_STATE " + array[num26].Name + " ARMED");
								}
								array2[num26] = text10;
							}
						}
					}
					if (DateTime.UtcNow >= dateTime3)
					{
						dateTime3 = DateTime.UtcNow.AddSeconds(5.0);
						bool flag9 = text4 == "ARMED" && patch2 != null && text6 == "ARMED";
						for (int num27 = 0; num27 < array2.Length; num27++)
						{
							flag9 = flag9 && array2[num27] == "ARMED";
						}
						localMcrService.SetNativeHealth(flag9, flag9 ? "TRANSPORT_READY_NATIVE_UNVERIFIED" : "NATIVE_HOOKS_DEGRADED");
						try
						{
							ulong value10 = 0uL;
							if (patch2 != null)
							{
								ReadU32(intPtr, patch2.StatusAddress + 36, out var value11, out error2);
								value10 = value11;
							}
							else if (reloadVtablePatch != null)
							{
								ReadU64(intPtr, reloadVtablePatch.CounterAddress, out value10, out error2);
							}
							Dictionary<string, string> dictionary = new Dictionary<string, string>();
							dictionary["protohttp_calls_approximate"] = DataU64(intPtr, hookSpec3, 0).ToString(CultureInfo.InvariantCulture);
							dictionary["relative_api_redirects_approximate"] = DataU64(intPtr, hookSpec3, 16).ToString(CultureInfo.InvariantCulture);
							dictionary["saved_locator_redirects_approximate"] = DataU64(intPtr, hookSpec3, 56).ToString(CultureInfo.InvariantCulture);
							dictionary["last_body_length"] = DataU64(intPtr, hookSpec3, 48).ToString(CultureInfo.InvariantCulture);
							dictionary["stock_lifecycle_successes"] = value10.ToString(CultureInfo.InvariantCulture);
							if (patch2 != null)
							{
								ReadU32(intPtr, patch2.StatusAddress, out var value12, out error2);
								ReadU32(intPtr, patch2.StatusAddress + 4, out var value13, out error2);
								ReadU32(intPtr, patch2.StatusAddress + 24, out var value14, out error2);
								ReadU32(intPtr, patch2.StatusAddress + 12, out var value15, out error2);
								ReadU32(intPtr, patch2.StatusAddress + 28, out var value16, out error2);
								ReadU32(intPtr, patch2.StatusAddress + 32, out var value17, out error2);
								dictionary["stock_lifecycle_callbacks"] = value12.ToString(CultureInfo.InvariantCulture);
								dictionary["stock_lifecycle_last_result"] = DurablePairSkipText(value13);
								dictionary["stock_lifecycle_last_slot"] = "0x" + value14.ToString("X", CultureInfo.InvariantCulture);
								dictionary["asset_key_fallback_calls"] = value15.ToString(CultureInfo.InvariantCulture);
								dictionary["stock_load_calls"] = value16.ToString(CultureInfo.InvariantCulture);
								dictionary["stock_reload_calls"] = value17.ToString(CultureInfo.InvariantCulture);
							}
							dictionary["note"] = "Diagnostic counters only. No URLs, authorization headers, or body memory are captured here.";
							RepairJson.AtomicWrite(Path.Combine(text2, "native-transport-counters.json"), RepairJson.Encode(dictionary));
						}
						catch
						{
						}
						if (flag5 != flag9)
						{
							action(flag9 ? "NATIVE HOOK HEALTH: ARMED; actual visual initialization still unverified." : "NATIVE HOOK HEALTH: DEGRADED; do not begin another import.");
							flag5 = flag9;
						}
					}
					if (!localMcrService.IsHealthy)
					{
						action("LOCAL SERVICE FAILURE: " + localMcrService.ErrorText + "; restoring owned hooks. Do not save a partial import.");
						localMcrService.SetNativeHealth(healthy: false, "SERVICE_FAILED");
						num2 = 23;
						break;
					}
					int completedImports;
					int requestCount;
					string text12;
					lock (localMcrService.Sync)
					{
						completedImports = localMcrService.CompletedImports;
						requestCount = localMcrService.RequestCount;
						text12 = localMcrService.GuardFailure ?? "";
					}
					if (requestCount != num14)
					{
						if (num14 >= 0 && requestCount > num14)
						{
							action("MCR HTTP request count: " + requestCount.ToString(CultureInfo.InvariantCulture));
						}
						num14 = requestCount;
					}
					if (completedImports != num13)
					{
						if (num13 >= 0 && completedImports > num13)
						{
							action("Uploaded artifact groups durably stored (NOT proof of native import). Store total: " + completedImports.ToString(CultureInfo.InvariantCulture));
						}
						num13 = completedImports;
					}
					if (text12.Length != 0 && text12 != text3)
					{
						text3 = text12;
						action("MCR GUARD FAILURE: " + text12);
						action("A request was rejected or interrupted. Do not treat this as a successful Dynasty import. Inspect loopback-mcr/requests.jsonl and the quarantine folder.");
					}
					Thread.Sleep(250);
				}
				if (num2 == 23)
				{
					action("Ending the runtime because its local service failed. The game may still be running; do not save an incomplete import.");
				}
				else if (PersistentCancelRequested)
				{
					action("Runtime shutdown requested.");
				}
				else
				{
					action("CollegeFB27.exe exited. Ending v1.1.17 runtime session.");
				}
				if (localMcrService != null)
				{
					StopLoopbackServer(localMcrService);
					try
					{
						WriteLoopbackServerSummary(text2, localMcrService);
					}
					catch (Exception ex7)
					{
						B("SUMMARY_WRITE_WARNING " + ex7.Message);
					}
					action("Session totals: persisted artifact groups (NOT game import confirmations)=" + localMcrService.CompletedImports.ToString(CultureInfo.InvariantCulture) + " HTTP requests=" + localMcrService.RequestCount.ToString(CultureInfo.InvariantCulture) + " guard failure=" + ((localMcrService.GuardFailure.Length == 0) ? "<none>" : localMcrService.GuardFailure));
				}
			}
			finally
			{
				if (log != null)
				{
					((IDisposable)log).Dispose();
				}
			}
		}
		catch (Exception ex8)
		{
			B("IMPORT_RUNTIME_FATAL " + ex8);
			try
			{
				File.AppendAllText(path, string.Concat("FATAL: ", ex8, Environment.NewLine));
			}
			catch
			{
			}
			Console.WriteLine("FATAL: " + ex8.Message);
			num2 = 99;
		}
		finally
		{
			try { BeforeRestore?.Invoke(); }
			catch (Exception cleanupError) { B("ASSET_CLEANUP_WARNING " + cleanupError.Message); }
			if (localMcrService != null)
			{
				try
				{
					StopLoopbackServer(localMcrService);
				}
				catch
				{
				}
			}
			bool flag10 = false;
			try
			{
				flag10 = !process.HasExited;
			}
			catch
			{
				flag10 = false;
			}
			if (flag10)
			{
				RestoreHook(intPtr, hookSpec3, out var error5);
				RestoreHook(intPtr, hookSpec2, out error5);
				RestoreHook(intPtr, hookSpec, out error5);
				RestoreDurablePairRepair(intPtr, patch2, out error5);
				RestoreHardReloadOverride(intPtr, reloadVtablePatch, out error5);
				RestoreCapacityVtableOverride(intPtr, patch, out error5);
			}
			if (localMcrService != null)
			{
				try
				{
					localMcrService.Dispose();
				}
				catch
				{
				}
			}
			CloseHandle(intPtr);
		}
		return num2;
	}
}
