#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$MmcRoot,
      [Parameter(Mandatory=$true)][string]$BuildDirectory,
      [Parameter(Mandatory=$true)][string]$WorkingFbmod,
      [Parameter(Mandatory=$true)][string]$ExpectedFbmodHash,
      [Parameter(Mandatory=$true)][string]$CacheIndex,
      [Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh package output directory.'}
$buildRoot=(Resolve-Path -LiteralPath $BuildDirectory).Path
$build=Get-Content -LiteralPath (Join-Path $buildRoot 'build-validation.json') -Raw|ConvertFrom-Json
$runtime=Join-Path $buildRoot ('OfflineTeamBuilderRuntime-'+$build.version)
& (Join-Path $PSScriptRoot 'tests\PackagingChecks.ps1') -ReleaseDirectory $runtime
if((Get-FileHash -LiteralPath (Join-Path $runtime 'Plugins\OfflineTeamBuilderRuntime.dll')).Hash -ine $build.managedSha256){throw 'Checked managed build changed.'}
if((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'Native\bin\Release\OfflineTeamBuilderNative.dll')).Hash -ine $build.nativeSha256){throw 'Native source-package input changed from checked build.'}
if(-not (Select-String -LiteralPath (Join-Path $buildRoot 'checks.log') -SimpleMatch ('PASS '+$build.managedChecks+' checks.') -Quiet)){throw 'Checked build result not found.'}
$fbmod=(Resolve-Path -LiteralPath $WorkingFbmod).Path
if((Get-FileHash -LiteralPath $fbmod).Hash -ine $ExpectedFbmodHash){throw 'Baseline mod identity changed.'}
$release=Join-Path $out ('OfflineTeamBuilder-'+$build.version)
New-Item -ItemType Directory -Path $release -Force|Out-Null
New-Item -ItemType Directory -Path (Join-Path $release 'Plugins') -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $runtime 'Plugins\OfflineTeamBuilderRuntime.dll') -Destination (Join-Path $release 'Plugins\OfflineTeamBuilderRuntime.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'USER-README.txt') -Destination (Join-Path $release 'README.txt')
$modOutput=Join-Path $release 'OfflineTeamBuilder.fbmod'
$metadata=& python (Join-Path $PSScriptRoot 'tools\rebrand_fbmod.py') $fbmod $modOutput --version $build.version --expected-source $ExpectedFbmodHash
if($LASTEXITCODE -ne 0){throw 'Metadata-only mod rewrite failed.'}
$modReport=($metadata -join "`n")|ConvertFrom-Json
if(-not $modReport.game_compatibility_unchanged -or -not $modReport.resource_records_unchanged -or -not $modReport.manifest_and_payloads_unchanged){throw 'Mod preservation check failed.'}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\VerifyFbmod.ps1') -MmcRoot $MmcRoot -Baseline $fbmod -Candidate $modOutput -Version $build.version
if($LASTEXITCODE -ne 0){throw 'Installed SDK rejected mod preservation check.'}
$project=Join-Path $out 'OfflineTeamBuilder.fbproject'
$projectReport=Join-Path $out 'project-verification.json'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tools\export-fbproject.ps1') -MmcRoot $MmcRoot -Fbmod $modOutput -CacheIndex $CacheIndex -Destination $project -ReportPath $projectReport
if($LASTEXITCODE -ne 0){throw 'Editable project export failed.'}
& python (Join-Path $PSScriptRoot 'tools\verify_fbproject.py') $project $projectReport
if($LASTEXITCODE -ne 0){throw 'Independent editable project verification failed.'}
# Keep verification metadata and editable assets outside the player ZIP.
$recordPath=Join-Path $out 'release.json'
$files=@(Get-ChildItem -LiteralPath $release -Recurse -File|Where-Object FullName -ne $recordPath|ForEach-Object{@{path=[IO.Path]::GetRelativePath($release,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
@{version=$build.version;authors='JDHalfRack / Ace / Timberline Tex';status='Distribution; single installation DLL; per-user data';managedChecks=$build.managedChecks;nativeSha256=$build.nativeSha256;files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $recordPath -Encoding UTF8
$zip=Join-Path $out ('OfflineTeamBuilder-'+$build.version+'.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($release,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
& (Join-Path $PSScriptRoot 'tests\PackagingChecks.ps1') -ReleaseDirectory $release -ZipPath $zip -PlayerPackage -RecordPath $recordPath
$source=Join-Path $out ('OfflineTeamBuilder-'+$build.version+'-source')
New-Item -ItemType Directory -Path $source|Out-Null
foreach($folder in @('src','Profiles','Native','tests','tools')){
    $from=Join-Path $PSScriptRoot $folder
    foreach($file in Get-ChildItem -LiteralPath $from -Recurse -File){
        $relative=[IO.Path]::GetRelativePath($PSScriptRoot,$file.FullName)
        if($relative -match '(^|[\\/])(bin|obj|__pycache__)([\\/]|$)'){continue}
        if($file.Extension -cnotin @('.cs','.csproj','.cpp','.h','.json','.ps1','.py','.md','.txt')){throw ('Unexpected source file: '+$relative)}
        $to=Join-Path $source $relative
        New-Item -ItemType Directory -Path (Split-Path $to -Parent) -Force|Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $to
    }
}
foreach($file in Get-ChildItem -LiteralPath $PSScriptRoot -File){
    if($file.Extension -cin @('.csproj','.ps1','.md','.txt','.json')){Copy-Item -LiteralPath $file.FullName -Destination $source}
}
$helperTarget=Join-Path $source 'Native\bin\Release'
New-Item -ItemType Directory -Path $helperTarget -Force|Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Native\bin\Release\OfflineTeamBuilderNative.dll') -Destination $helperTarget
# Include a portable recipe input, not a complete game cache or baseline mod.
$cache=Get-Content -LiteralPath $CacheIndex -Raw|ConvertFrom-Json
@{head=$cache.head;superbundles=$cache.superbundles}|ConvertTo-Json -Depth 3|Set-Content -LiteralPath (Join-Path $source 'package-cache-metadata.json') -Encoding UTF8
$sourceZip=Join-Path $out ('OfflineTeamBuilder-'+$build.version+'-source.zip')
[IO.Compression.ZipFile]::CreateFromDirectory($source,$sourceZip,[IO.Compression.CompressionLevel]::Optimal,$false)
$sourceArchive=[IO.Compression.ZipFile]::OpenRead($sourceZip)
try{
    $sourceFiles=@(Get-ChildItem -LiteralPath $source -Recurse -File)
    $entries=@($sourceArchive.Entries|Where-Object Name)
    if($entries.Count -ne $sourceFiles.Count){throw 'Source ZIP count differs.'}
    foreach($entry in $entries){
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try{$digest=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','')}finally{$stream.Dispose();$sha.Dispose()}
        if($digest -ine (Get-FileHash -LiteralPath (Join-Path $source $entry.FullName)).Hash){throw ('Source ZIP bytes differ: '+$entry.FullName)}
    }
}finally{$sourceArchive.Dispose()}
@{version=$build.version;status='Distribution package';managedChecks=$build.managedChecks;managedSha256=$build.managedSha256;nativeSha256=$build.nativeSha256;userZipSha256=(Get-FileHash -LiteralPath $zip).Hash;sourceZipSha256=(Get-FileHash -LiteralPath $sourceZip).Hash;modPreservation=$modReport;sourceZipFiles=$sourceFiles.Count;userZipFiles=$files.Count;gameplayAcceptance=$build.inGameAcceptance;installedFilesChanged=$false}|
    ConvertTo-Json -Depth 6|Set-Content -LiteralPath (Join-Path $out 'package-verification.json') -Encoding UTF8
Write-Output ('User package: '+$zip)
Write-Output ('Matching source: '+$sourceZip)
