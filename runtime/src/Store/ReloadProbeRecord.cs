using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
internal sealed class ReloadProbeRecord
{
	[DataMember]
	public string Utc;

	[DataMember]
	public string Event;

	[DataMember]
	public ulong NativeRequestCalls;

	[DataMember]
	public ulong RelativeApiRedirects;

	[DataMember]
	public ulong HardReimportCalls;

	[DataMember]
	public ulong KeyGateCalls;

	[DataMember]
	public ulong OwnerGateCalls;

	[DataMember]
	public ulong OwnerGateFailureCode;

	[DataMember]
	public int LocalHttpRequests;

	[DataMember]
	public int StoredGroups;

	[DataMember]
	public bool ServiceAlive;

	[DataMember]
	public string CapacityState;

	[DataMember]
	public string SafeUrlSample;

	[DataMember]
	public string UrlSampleStatus;

	[DataMember]
	public string Scope = "Read-only counters from the installed reload and transport bridges; URL pointer samples can miss bursts. No save data is rewritten.";
}
