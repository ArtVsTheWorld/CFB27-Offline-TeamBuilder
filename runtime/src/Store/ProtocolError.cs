using System;

namespace OfflineTeamBuilder.Repair;

internal sealed class ProtocolError : Exception
{
	public readonly int Status;

	public ProtocolError(int status, string message)
		: base(message)
	{
		Status = status;
	}
}
