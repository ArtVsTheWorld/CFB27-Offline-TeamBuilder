using System.Reflection;
using System.Runtime.InteropServices;
using Frosty.Core.Attributes;
using OfflineTeamBuilderRuntime;
[assembly: AssemblyTitle(PluginIdentity.DisplayName)]
[assembly: AssemblyProduct(PluginIdentity.DisplayName)]
[assembly: AssemblyDescription("Unified offline TeamBuilder import, Dynasty asset restoration, news previews and stadium audio. Native-contract compatibility; no executable checksum requirement.")]
[assembly: AssemblyVersion(PluginIdentity.AssemblyVersion)]
[assembly: AssemblyFileVersion(PluginIdentity.AssemblyVersion)]
[assembly: AssemblyInformationalVersion(PluginIdentity.Version)]
[assembly: PluginDisplayName(PluginIdentity.DisplayName)]
[assembly: PluginVersion(PluginIdentity.Version)]
[assembly: PluginAuthor(PluginIdentity.Authors)]
[assembly: RegisterExecutionAction(typeof(OfflineTeamBuilderRuntime.RuntimeAction))]
[assembly: ComVisible(false)]
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("RuntimeChecks")]
