[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MmcRoot,
    [Parameter(Mandatory=$true)][string]$Fbmod,
    [Parameter(Mandatory=$true)][string]$CacheIndex,
    [Parameter(Mandatory=$true)][string]$Destination,
    [Parameter(Mandatory=$true)][string]$ReportPath
)
$ErrorActionPreference='Stop'
if ((Test-Path -LiteralPath $Destination) -or (Test-Path -LiteralPath $ReportPath)) { throw 'Project/report destination already exists.' }
$manager=(Resolve-Path -LiteralPath $MmcRoot).Path
foreach($name in @('FrostyHash.dll','FrostySdk.dll','FrostyCore.dll')) { [Reflection.Assembly]::LoadFrom((Join-Path $manager $name)) | Out-Null }
$profile=New-Object FrostySdk.Profile
$profile.Name='CollegeFB27';$profile.DataVersion=[int][FrostySdk.ProfileVersion]::CollegeFootball27
[FrostySdk.ProfilesLibrary].GetField('m_effectiveProfile',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$profile)
# Read just the required metadata into PowerShell; never print/distribute the
# full local SDK asset index. No game process or editor session is accessed.
$indexText=& python -c 'import json,sys; p=json.load(open(sys.argv[1])); print(json.dumps(dict(head=p.get(sys.argv[2]),superbundles=p.get(sys.argv[3]))))' $CacheIndex head superbundles
if ($LASTEXITCODE -ne 0) { throw 'Unable to read cache index.' }
$index=($indexText -join "`n") | ConvertFrom-Json
$reader=[Frosty.Core.IO.FrostyModReader]::new([IO.File]::OpenRead((Resolve-Path -LiteralPath $Fbmod).Path))
$memory=[IO.MemoryStream]::new();$writer=[IO.BinaryWriter]::new($memory)
function Text([string]$value) { $writer.Write([Text.Encoding]::UTF8.GetBytes($value));$writer.Write([byte]0) }
function Blob([byte[]]$value) { $writer.Write([int]$value.Length);if($value.Length){$writer.Write($value)} }
function Sha([byte[]]$value) { $sha=[Security.Cryptography.SHA1]::Create();try { return ,$sha.ComputeHash($value) } finally {$sha.Dispose()} }
$chunks=@();$collectors=@();$details=$null
try {
    if (!$reader.IsValid -or $reader.GameVersion -ne $index.head) { throw 'Mod/cache profile or head mismatch.' }
    $details=$reader.ReadModDetails();$resources=$reader.ReadResources()
    foreach($resource in $resources) {
        if ($resource.Type.ToString() -eq 'Embedded') { continue }
        if ($resource.Type.ToString() -ne 'Chunk' -or $resource.ResourceIndex -lt 0) { throw 'Unsupported project resource.' }
        if ($resource.HasHandler) {
            if ($resource.Handler -ne -1113851035 -or !$resource.UserData.StartsWith('legacy;Collector (')) { throw 'Unexpected custom handler.' }
            $collectors+=@($resource);continue
        }
        if (!$resource.IsAdded -or !$resource.UserData.StartsWith('legacy;') -or @($resource.AddedBundles).Count) { throw 'Unexpected legacy chunk contract.' }
        $entry=New-Object FrostySdk.Managers.Entries.ChunkAssetEntry
        $resource.FillAssetEntry($entry)
        # MMC's reader decodes FMENC payloads into editable compressed chunks.
        $payload=$reader.GetResourceData($resource)
        if ([BitConverter]::ToString((Sha $payload)).Replace('-','').ToLowerInvariant() -cne $resource.Sha1.ToString().ToLowerInvariant()) { throw 'Decoded chunk SHA1 mismatch.' }
        $chunks+=@([pscustomobject]@{Name=$resource.UserData.Substring(7);Entry=$entry;Payload=$payload})
    }
    if ($chunks.Count -ne 3 -or $collectors.Count -ne 3 -or @($chunks.Name | Select-Object -Unique).Count -ne 3) { throw 'Expected three active legacy movies and three collectors.' }
    # Every collector link must name one of exactly these active chunks and
    # match its sizes. Unreferenced historical manifest blobs are not imported.
    $links=0
    foreach($collector in $collectors) {
        $data=$reader.GetResourceData($collector)
        if ($data.Length % 52) { throw 'Invalid legacy collector merge length.' }
        for($at=0;$at -lt $data.Length;$at+=52) {
            $guidBytes=New-Object byte[] 16;[Array]::Copy($data,$at+4,$guidBytes,0,16)
            $guid=[Guid]::new($guidBytes);$match=@($chunks | Where-Object {$_.Entry.Id -eq $guid})
            if ($match.Count -ne 1) { throw 'Collector references a chunk absent from the editable project.' }
            $entry=$match[0].Entry
            if ([BitConverter]::ToInt32($data,$at) -ne $entry.H32 -or [BitConverter]::ToInt64($data,$at+20) -ne 0 -or [BitConverter]::ToInt64($data,$at+28) -ne 0 -or [BitConverter]::ToInt64($data,$at+36) -ne $match[0].Payload.Length -or [BitConverter]::ToInt64($data,$at+44) -ne $entry.LogicalSize) { throw 'Collector/chunk size or hash mismatch.' }
            $links++
        }
    }
    $writer.Write([Text.Encoding]::ASCII.GetBytes("FROSTY`0`0"));$writer.Write([uint32]18);Text 'CollegeFB27'
    $ticks=[DateTime]::UtcNow.Ticks;$writer.Write([long]$ticks);$writer.Write([long]$ticks);$writer.Write([uint32]$reader.GameVersion)
    foreach($value in @($details.Title,$details.Author,$details.Category,$details.Version,$details.Description)) { Text $value }
    foreach($name in @('Icon','Screenshot0','Screenshot1','Screenshot2','Screenshot3')) {
        $image=@($resources | Where-Object Name -eq $name)
        $bytes=if($image.Count -eq 1 -and $image[0].ResourceIndex -ge 0){$reader.GetResourceData($image[0])}else{[byte[]]@()}
        Blob $bytes
    }
    1..4 | ForEach-Object {$writer.Write([int]0)} # no new superbundles/bundles/EBX/RES
    $writer.Write([int]$chunks.Count)
    foreach($chunk in $chunks) {$writer.Write($chunk.Entry.Id.ToByteArray());$writer.Write([int]$chunk.Entry.H32);$writer.Write([long]$chunk.Entry.H64)}
    $writer.Write([int]0);$writer.Write([int]0) # modified EBX/RES
    $writer.Write([int]$chunks.Count)
    foreach($chunk in $chunks) {
        $e=$chunk.Entry;$writer.Write($e.Id.ToByteArray());$writer.Write([int]0)
        $writer.Write([int]$e.AddedSuperBundles.Count)
        foreach($id in $e.AddedSuperBundles) {if($id -lt 0 -or $id -ge $index.superbundles.Count){throw 'Superbundle index out of range.'};Text $index.superbundles[$id]}
        $writer.Write([int]$e.H32);$writer.Write([long]$e.H64);$writer.Write([bool]$true)
        $writer.Write([byte[]](Sha $chunk.Payload));$writer.Write([uint32]$e.LogicalOffset);$writer.Write([uint32]$e.LogicalSize)
        $writer.Write([uint32]$e.RangeStart);$writer.Write([uint32]$e.RangeEnd);$writer.Write([int]$e.FirstMip)
        $writer.Write([int]$e.H32);$writer.Write([long]$e.H64);$writer.Write([bool]$true)
        Text ('legacy;'+$chunk.Name);Blob $chunk.Payload
    }
    $writer.Write([int]2);Text 'legacy';$writer.Write([int]$chunks.Count)
    foreach($chunk in $chunks) {
        Text $chunk.Name;$writer.Write([int]1);Text 'chunk';$writer.Write($chunk.Entry.Id.ToByteArray())
        $writer.Write($chunk.Entry.Id.ToByteArray());$writer.Write([long]0);$writer.Write([long]0)
        $writer.Write([long]$chunk.Payload.Length);$writer.Write([long]$chunk.Entry.LogicalSize)
    }
    Text 'fs';$writer.Write([int]0);$writer.Flush()
    [IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Destination),$memory.ToArray())
    $summary=@{format=18;profile='CollegeFB27';head=$reader.GameVersion;title=$details.Title;authors=$details.Author;version=$details.Version;collectorLinks=$links;chunks=@($chunks | ForEach-Object {@{name=$_.Name;guid=$_.Entry.Id.ToString();payloadSha1=[BitConverter]::ToString((Sha $_.Payload)).Replace('-','').ToLowerInvariant();payloadBytes=$_.Payload.Length;logicalSize=$_.Entry.LogicalSize;h32=$_.Entry.H32;h64=$_.Entry.H64;superbundles=@($_.Entry.AddedSuperBundles | ForEach-Object {$index.superbundles[$_]})}})}
    $summary | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
} catch { Write-Host $_.ScriptStackTrace;throw } finally {$writer.Dispose();$memory.Dispose();$reader.Dispose()}
Write-Output 'PASS: v18 editable project contains all three active movies and every working collector link; no installed files changed.'
