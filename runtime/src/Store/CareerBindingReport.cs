using System.Collections.Generic;
using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
public sealed class CareerBindingReport
{
	[DataMember]
	public string SourceSha256;

	[DataMember]
	public string OutputSha256;

	[DataMember]
	public int SchemaMajor;

	[DataMember]
	public int PayloadLength;

	[DataMember]
	public List<CareerUrlChange> Changes = new List<CareerUrlChange>();

	[DataMember]
	public bool AllOtherPayloadBytesUnchanged;

	[DataMember]
	public bool GameAcceptanceOfAbsoluteUrlsVerified;

	[DataMember]
	public string Scope = "Opt-in COPY only; URL fields rebound to fixed localhost endpoint. No roster/team reimport. Requires in-game validation of URL handling.";
}
