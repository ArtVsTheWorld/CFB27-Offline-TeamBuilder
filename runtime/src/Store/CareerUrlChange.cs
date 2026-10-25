using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
public sealed class CareerUrlChange
{
	[DataMember]
	public int Row;

	[DataMember]
	public string Field;

	[DataMember]
	public string ArtifactId;

	[DataMember]
	public string Before;

	[DataMember]
	public string After;

	[DataMember]
	public int PayloadOffset;

	[DataMember]
	public int SlotBytes = 501;
}
