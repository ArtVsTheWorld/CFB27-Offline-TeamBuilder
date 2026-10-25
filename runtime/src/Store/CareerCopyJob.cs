using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
public sealed class CareerCopyJob
{
	[DataMember]
	public string Source;

	[DataMember]
	public string Destination;
}
