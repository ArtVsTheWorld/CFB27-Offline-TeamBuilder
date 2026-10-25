using System;

namespace OfflineTeamBuilder.Repair;

internal sealed class MetadataRecord
{
	public string Id;

	public string Kind;

	public string Group;

	public byte[] OriginalJson;

	public DateTime SeenUtc = DateTime.UtcNow;
}
