#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$MmcRoot,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts'),[switch]$RebuildNative,[switch]$RebuildArtworkNative)
$ErrorActionPreference='Stop'
$manager=(Resolve-Path -LiteralPath $MmcRoot).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh output directory; preserve prior checkpoints.'}
if($RebuildNative){& (Join-Path $PSScriptRoot 'Native\BuildNative.ps1');if($LASTEXITCODE -ne 0){throw 'Native build failed.'}}
$native=Join-Path $PSScriptRoot 'Native\bin\Release\OfflineTeamBuilderNative.dll'
if(-not(Test-Path -LiteralPath $native)) {
    $distributed=Join-Path $PSScriptRoot '..\dist\v2.4.14\OfflineTeamBuilderNative.dll'
    if(-not(Test-Path -LiteralPath $distributed)){throw 'Native build input missing. Use -RebuildNative with MSVC, or supply the checked source-package helper.'}
    New-Item -ItemType Directory -Path (Split-Path $native -Parent) -Force|Out-Null
    Copy-Item -LiteralPath $distributed -Destination $native
}
$manifestPath=Join-Path $PSScriptRoot 'Native\native-helper-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $native).Hash -ine $manifest.sha256){throw 'Native helper does not match its embedded manifest.'}
$codec=Join-Path $PSScriptRoot 'ArtworkNative\bin\OfflineTeamBuilder.ArtworkCodec.dll'
$distributedCodec=Join-Path $PSScriptRoot '..\dist\v2.4.14\OfflineTeamBuilder.ArtworkCodec.dll'
if(!$RebuildArtworkNative -and !(Test-Path -LiteralPath $codec) -and (Test-Path -LiteralPath $distributedCodec)){
    New-Item -ItemType Directory -Path (Split-Path $codec -Parent) -Force|Out-Null
    Copy-Item -LiteralPath $distributedCodec -Destination $codec
}
if($RebuildArtworkNative -or !(Test-Path -LiteralPath $codec)){
    & (Join-Path $PSScriptRoot 'ArtworkNative\build.ps1')
    if($LASTEXITCODE -ne 0){throw 'Artwork codec build failed.'}
}
if(!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ArtworkNative\templates.zip'))){throw 'Native artwork templates are missing. Restore templates.zip from the source checkpoint.'}
& dotnet build (Join-Path $PSScriptRoot 'Host\OfflineTeamBuilder.Host.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw 'Detached host build failed.'}
$hostBinary=Join-Path $PSScriptRoot 'Host\bin\Release\net48\OfflineTeamBuilder.Host.exe'
$hostVersion=[Diagnostics.FileVersionInfo]::GetVersionInfo($hostBinary).ProductVersion
@{version=$hostVersion;sha256=(Get-FileHash -LiteralPath $hostBinary).Hash.ToLowerInvariant()}|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $PSScriptRoot 'Host\host-manifest.json') -Encoding UTF8
New-Item -ItemType Directory -Path $out|Out-Null
& dotnet build (Join-Path $PSScriptRoot 'tests\RuntimeChecks.csproj') -c Release --nologo "-p:MmcRoot=$manager" 2>&1|Tee-Object -FilePath (Join-Path $out 'build.log')
if($LASTEXITCODE -ne 0){throw 'Managed build failed.'}
$env:OFFLINETB_CHECK_HOST=$hostBinary
$checkOutput=& (Join-Path $PSScriptRoot 'tests\bin\Release\net48\RuntimeChecks.exe') 2>&1
$checkExit=$LASTEXITCODE
Remove-Item Env:OFFLINETB_CHECK_HOST
$checkOutput|Tee-Object -FilePath (Join-Path $out 'checks.log')
if($checkExit -ne 0 -or ($checkOutput -join "`n") -notmatch 'PASS (\d+) checks\.'){throw 'Runtime checks failed.'}
$checks=[int]$Matches[1]
& (Join-Path $PSScriptRoot 'tests\SupportChecks.ps1') -Collector (Join-Path $PSScriptRoot 'collect-diagnostics.ps1')
$dll=Join-Path $PSScriptRoot 'bin\Release\net48\OfflineTeamBuilderRuntime.dll'
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'Expected semantic release version.'}
$release=Join-Path $out ('OfflineTeamBuilderRuntime-'+$version)
New-Item -ItemType Directory -Path (Join-Path $release 'Plugins') -Force|Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $release 'Plugins\OfflineTeamBuilderRuntime.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'USER-README.txt') -Destination (Join-Path $release 'README.txt')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'TESTING.txt') -Destination $release
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'collect-diagnostics.ps1') -Destination (Join-Path $release 'Collect-Support.ps1')
$files=@(Get-ChildItem -LiteralPath $release -Recurse -File|ForEach-Object{@{path=[IO.Path]::GetRelativePath($release,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName).Hash.ToLowerInvariant()}})
@{version=$version;status='Distribution; single installation DLL; per-user runtime data';managedChecks=$checks;nativeSha256=$manifest.sha256;files=$files}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $release 'release.json') -Encoding UTF8
& (Join-Path $PSScriptRoot 'tests\PackagingChecks.ps1') -ReleaseDirectory $release
@{version=$version;managedChecks=$checks;supportCollectorChecks=$true;managedSha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();nativeSha256=$manifest.sha256;runtimeDirectory=$release;inGameAcceptance='Detached host candidate; private lifecycle/server checks passed; game acceptance pending.'}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'build-validation.json') -Encoding UTF8
Write-Output ('Checked runtime staged: '+$release)
