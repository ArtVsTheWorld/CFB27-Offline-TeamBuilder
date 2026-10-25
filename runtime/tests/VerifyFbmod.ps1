[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MmcRoot,
    [Parameter(Mandatory=$true)][string]$Baseline,
    [Parameter(Mandatory=$true)][string]$Candidate,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$manager=(Resolve-Path -LiteralPath $MmcRoot).Path
foreach($name in @('FrostyHash.dll','FrostySdk.dll','FrostyCore.dll')) { [Reflection.Assembly]::LoadFrom((Join-Path $manager $name)) | Out-Null }
# Reader-only profile in this private test process. Does not initialize MMC,
# load a game installation/cache, attach to the game, or write installed files.
$profile=New-Object FrostySdk.Profile
$profile.Name='CollegeFB27'
$profile.DataVersion=[int][FrostySdk.ProfileVersion]::CollegeFootball27
[FrostySdk.ProfilesLibrary].GetField('m_effectiveProfile',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$profile)
$before=[Frosty.Core.IO.FrostyModReader]::new([IO.File]::OpenRead((Resolve-Path -LiteralPath $Baseline).Path))
$after=[Frosty.Core.IO.FrostyModReader]::new([IO.File]::OpenRead((Resolve-Path -LiteralPath $Candidate).Path))
function Digest([byte[]]$Data) {
    if ($null -eq $Data) { return '<null>' }
    $sha=[Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($Data)) } finally { $sha.Dispose() }
}
try {
    if (!$before.IsValid -or !$after.IsValid) { throw 'MMC rejected the fbmod profile/header.' }
    if ($before.GameVersion -ne $after.GameVersion -or $before.GameSha1.ToString() -cne $after.GameSha1.ToString()) { throw 'Game compatibility fields changed.' }
    $oldDetails=$before.ReadModDetails();$details=$after.ReadModDetails()
    if ($details.Title -cne 'Offline TeamBuilder' -or $details.Author -cne 'JDHalfRack / Ace / Timberline Tex' -or $details.Version -cne $Version) { throw 'MMC read incorrect display details.' }
    $oldResources=$before.ReadResources();$resources=$after.ReadResources()
    if ($resources.Length -ne $oldResources.Length -or $resources.Length -ne 11) { throw 'Resource count changed.' }
    $payloads=0
    for($i=0;$i -lt $resources.Length;$i++) {
        if ($resources[$i].Name -cne $oldResources[$i].Name -or $resources[$i].ResourceIndex -ne $oldResources[$i].ResourceIndex -or $resources[$i].GetType() -ne $oldResources[$i].GetType()) { throw "MMC resource record differs: $i" }
        $original=$before.GetResourceData($oldResources[$i]);$data=$after.GetResourceData($resources[$i])
        if ((Digest $original) -cne (Digest $data)) { throw "MMC-decoded payload differs: $i" }
        if ($null -ne $data) { $payloads++ }
    }
    Write-Output "PASS: MMC reads new name, credits and version; all $($resources.Length) resources and $payloads decoded payloads match the working fbmod."
} finally { $before.Dispose();$after.Dispose() }
