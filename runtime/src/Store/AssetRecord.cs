using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
public sealed class AssetRecord
{
	[DataMember]
	public int Schema = 1;

	[DataMember]
	public string Id;

	[DataMember]
	public string Kind;

	[DataMember]
	public string Group;

	[DataMember]
	public string Sha256;

	[DataMember]
	public long Length;

	[DataMember]
	public string Validation;

	[DataMember]
	public string CreatedUtc;

	[DataMember]
	public List<string> UploadPaths = new List<string>();
}
