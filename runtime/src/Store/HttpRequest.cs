using System;
using System.Collections.Generic;

namespace OfflineTeamBuilder.Repair;

internal sealed class HttpRequest
{
	public string Method;

	public string Path;

	public string TargetForDiagnostics;

	public readonly Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	public long? Length;

	public bool Chunked;

	public bool ContinueExpected;
}
