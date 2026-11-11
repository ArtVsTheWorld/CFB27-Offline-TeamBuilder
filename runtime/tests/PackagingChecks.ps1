#requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,[string]$ZipPath)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $ReleaseDirectory).Path
$record=Get-Content -LiteralPath (Join-Path $root 'release.json') -Raw|ConvertFrom-Json
$paths=@($record.files.path)
if(@($paths|Group-Object|Where-Object Count -gt 1).Count){throw 'Duplicate package paths.'}
$required=@('Plugins/OfflineTeamBuilderRuntime.dll','README.txt','TESTING.txt','Collect-Support.ps1')
foreach($path in $required){if($paths -cnotcontains $path){throw ('Required package file missing: '+$path)}}
$allowed=$required+@('OfflineTeamBuilder.fbmod','OfflineTeamBuilder.fbproject','VALIDATION.txt')
foreach($file in $record.files){
    if($file.path -cnotin $allowed){throw ('Unexpected package entry: '+$file.path)}
    if((Get-FileHash -LiteralPath (Join-Path $root $file.path)).Hash -ine $file.sha256){throw ('Package bytes differ: '+$file.path)}
}
$actual=@(Get-ChildItem -LiteralPath $root -Recurse -File|ForEach-Object{[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')})
if(@(Compare-Object ($actual|Sort-Object) (@($paths+'release.json')|Sort-Object)).Count){throw 'Unlisted files in package.'}
$dll=Join-Path $root 'Plugins\OfflineTeamBuilderRuntime.dll'
if([Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion -cne $record.version){throw 'Managed version differs from release manifest.'}
if(@(Get-ChildItem -LiteralPath (Join-Path $root 'Plugins') -Recurse -File).Count -ne 1){throw 'Single-install package contains additional plugin files.'}
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($dll))
$payload=$assembly.GetManifestResourceStream('OfflineTeamBuilderRuntime.NativePayload.dll')
$manifestStream=$assembly.GetManifestResourceStream('OfflineTeamBuilderRuntime.NativeManifest.json')
if($null -eq $payload -or $null -eq $manifestStream){throw 'Embedded native component or manifest missing.'}
$reader=[IO.StreamReader]::new($manifestStream);$sha=[Security.Cryptography.SHA256]::Create()
try{
    $nativeManifest=$reader.ReadToEnd()|ConvertFrom-Json
    $payloadHash=[BitConverter]::ToString($sha.ComputeHash($payload)).Replace('-','')
    if($payloadHash -ine $nativeManifest.sha256 -or $payloadHash -ine $record.nativeSha256){throw 'Embedded native helper/manifest mismatch.'}
    if($nativeManifest.abi -ne 1 -or $nativeManifest.statusSize -ne 128 -or $nativeManifest.registryEntries -ne 14){throw 'Embedded native ABI mismatch.'}
}finally{$reader.Dispose();$payload.Dispose();$sha.Dispose()}
if($ZipPath){
    $archive=[IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($ZipPath))
    try{
        $entries=@($archive.Entries|Where-Object Name)
        if(@(Compare-Object (@($entries.FullName|ForEach-Object{$_.Replace('\','/')})|Sort-Object) ($actual|Sort-Object)).Count){throw 'ZIP contents differ from staged files.'}
        foreach($entry in $entries){
            $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
            try{$digest=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
            if($digest -ine (Get-FileHash -LiteralPath (Join-Path $root $entry.FullName)).Hash){throw ('ZIP bytes differ: '+$entry.FullName)}
        }
    }finally{$archive.Dispose()}
}
Write-Output ('PASS: single installation DLL, embedded native integrity/ABI, versions and all file hashes'+$(if($ZipPath){', including ZIP readback.'}else{'.'}))
