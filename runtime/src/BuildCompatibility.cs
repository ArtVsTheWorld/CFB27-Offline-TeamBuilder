// Kept as an internal engine adapter, not a second version table.
namespace OfflineTeamBuilder.Repair;
internal static class BuildCompatibility
{
    internal static bool MayValidateModule(int size) { return OfflineTeamBuilderRuntime.GameProfile.Current.FitsImage(size); }
    internal static bool TryGetExpectedSingletonRva(int size,out long rva)
    { rva=MayValidateModule(size)?OfflineTeamBuilderRuntime.GameProfile.Current.Rva("ServiceGlobal"):0; return rva!=0; }
    internal static bool MatchesSingletonProfile(int size,long rva)
    { long expected; return TryGetExpectedSingletonRva(size,out expected)&&expected==rva; }
    internal static string Describe(int size)
    { return OfflineTeamBuilderRuntime.GameProfile.Current.id+" imageBounds=0x"+size.ToString("X")+"; executable checksums are not used; live signatures and vtables required"; }
}
