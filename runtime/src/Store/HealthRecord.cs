using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
internal sealed class HealthRecord
{
	[DataMember]
	public string Build = "1.1.17-stock-saved-asset-lifecycle";

	[DataMember]
	public bool ServiceAlive;

	[DataMember]
	public bool NativeHooksHealthy;

	[DataMember]
	public string State;

	[DataMember]
	public string NativeVisualInitialization = "NOT OBSERVED BY THIS BUILD";

	[DataMember]
	public string StoreDirectory;

	[DataMember]
	public int StoredCompleteArtifactGroups;

	[DataMember]
	public int Requests;

	[DataMember]
	public int Errors;

	[DataMember]
	public string LastError;

	[DataMember]
	public string UpdatedUtc;
}
