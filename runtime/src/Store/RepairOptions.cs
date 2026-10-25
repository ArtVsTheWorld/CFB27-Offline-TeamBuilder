using System.IO;
using System.Runtime.Serialization;

namespace OfflineTeamBuilder.Repair;

[DataContract]
public sealed class RepairOptions
{
	[DataMember]
	public long MaximumUploadBytes = 67108864L;

	[DataMember]
	public int MaximumMetadataBytes = 1048576;

	[DataMember]
	public long MaximumInflatedBytes = 67108864L;

	[DataMember]
	public long MaximumStoreBytes = 2147483648L;

	[DataMember]
	public long MaximumQuarantineBytes = 268435456L;

	[DataMember]
	public int MaximumClients = 4;

	[DataMember]
	public int IdleTimeoutMilliseconds = 30000;

	[DataMember]
	public int RequestDeadlineMilliseconds = 120000;

	[DataMember]
	public string StoreDirectory;

	[DataMember]
	public bool EnableUploadPathReadback = true;

	[DataMember]
	public CareerCopyJob[] CareerCopies;

	[OnDeserializing]
	private void Defaults(StreamingContext context)
	{
		MaximumUploadBytes = 67108864L;
		MaximumMetadataBytes = 1048576;
		MaximumInflatedBytes = 67108864L;
		MaximumStoreBytes = 2147483648L;
		MaximumQuarantineBytes = 268435456L;
		MaximumClients = 4;
		IdleTimeoutMilliseconds = 30000;
		RequestDeadlineMilliseconds = 120000;
		EnableUploadPathReadback = true;
	}

	public void Validate()
	{
		if (MaximumUploadBytes < 1024 || MaximumUploadBytes > 268435456 || MaximumMetadataBytes < 256 || MaximumMetadataBytes > 4194304 || MaximumInflatedBytes < 1024 || MaximumInflatedBytes > 268435456 || MaximumStoreBytes < MaximumUploadBytes || MaximumClients < 1 || MaximumClients > 16 || IdleTimeoutMilliseconds < 1000 || IdleTimeoutMilliseconds > 120000 || RequestDeadlineMilliseconds < IdleTimeoutMilliseconds || RequestDeadlineMilliseconds > 600000)
		{
			throw new InvalidDataException("Invalid Offline TeamBuilder repair limits.");
		}
	}
}
