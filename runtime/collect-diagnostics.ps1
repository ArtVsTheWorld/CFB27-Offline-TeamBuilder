[CmdletBinding()]
param([string]$MmcRoot,[string]$OutputDirectory,[string]$DiagnosticsRoot)
$ErrorActionPreference='Stop'
$local=[Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
if([string]::IsNullOrWhiteSpace($local)){throw 'Windows did not provide Local AppData; no installation folders were modified.'}
$userRoot=Join-Path $local 'Ace\OfflineTeamBuilder'
if(-not $DiagnosticsRoot){$DiagnosticsRoot=$userRoot}
if(-not $OutputDirectory){$OutputDirectory=Join-Path $userRoot 'Support'}
$data=[IO.Path]::GetFullPath($DiagnosticsRoot);$out=[IO.Path]::GetFullPath($OutputDirectory)
if($MmcRoot){
    $manager=[IO.Path]::GetFullPath($MmcRoot).TrimEnd('\')
    if($out.Equals($manager,[StringComparison]::OrdinalIgnoreCase)-or $out.StartsWith($manager+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Support output must be outside the MMC installation.'}
}
New-Item -ItemType Directory -Path $out -Force|Out-Null
$stage=Join-Path $out ('review-'+[Guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $stage|Out-Null
$included=@();$skipped=@()
# Explicit allow-list. No settings, repair options, migration copies, assets,
# saves, process dumps, executables, HTTP bodies or user cache directories.
foreach($name in @('runtime.log','runtime.log.1','runtime.log.2','status.json','status.txt','environment.json','game-context.json')){
    $path=Join-Path $data $name
    if(-not(Test-Path -LiteralPath $path -PathType Leaf)){continue}
    try{
        $item=Get-Item -LiteralPath $path
        if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0 -or $item.Length -gt 8MB){$skipped+=,$name;continue}
        Copy-Item -LiteralPath $path -Destination (Join-Path $stage $name);$included+=,$name
    }catch{$skipped+=,$name;Write-Warning ('Could not copy '+$name+': '+$_.Exception.Message)}
}
$native=Join-Path $data 'NativeLogs'
if(Test-Path -LiteralPath $native -PathType Container){
    if(((Get-Item -LiteralPath $native).Attributes -band [IO.FileAttributes]::ReparsePoint)-eq 0){
        foreach($item in @(Get-ChildItem -LiteralPath $native -Filter '*.log' -File|Where-Object {$_.Name -match '^\d+-\d+\.log$'}|Sort-Object LastWriteTimeUtc -Descending|Select-Object -First 2)){
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint)-ne 0){continue}
            $stream=$null
            try{
                $stream=[IO.File]::Open($item.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                $count=[int][Math]::Min(256KB,$stream.Length);$null=$stream.Seek(-$count,[IO.SeekOrigin]::End)
                $bytes=New-Object byte[] $count;$read=0
                while($read -lt $count){$n=$stream.Read($bytes,$read,$count-$read);if($n -eq 0){break};$read+=$n}
                $name='native-'+$item.Name+'.tail.txt';[IO.File]::WriteAllText((Join-Path $stage $name),[Text.Encoding]::UTF8.GetString($bytes,0,$read),[Text.UTF8Encoding]::new($false));$included+=,$name
            }catch{$skipped+=,$item.Name}finally{if($stream){$stream.Dispose()}}
        }
    }
}
@{schemaVersion=2;collectedUtc=[DateTime]::UtcNow.ToString('O');diagnosticsRoot=$data;included=$included;skipped=$skipped;
    privacy='Current diagnostics and bounded native-log tails only; review local paths before sharing. No files uploaded.'}|
    ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $stage 'support.json') -Encoding UTF8
$zip=Join-Path $out ('OfflineTeamBuilder-support-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6)+'.zip')
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($stage,$zip)
Write-Output ('Support ZIP: '+$zip)
Write-Output ('Review copies: '+$stage)
Write-Output 'Review for personal paths before sharing. No data was uploaded; settings, saves, assets and DLLs are excluded.'
