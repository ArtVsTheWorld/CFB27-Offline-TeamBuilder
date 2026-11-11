#requires -Version 7.0
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$MmcRoot,
      [string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts'),[switch]$RebuildNative)
$ErrorActionPreference='Stop'
$manager=(Resolve-Path -LiteralPath $MmcRoot).Path
$out=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $out){throw 'Use a fresh output directory; preserve prior checkpoints.'}
if($RebuildNative){& (Join-Path $PSScriptRoot 'Native\BuildNative.ps1');if($LASTEXITCODE -ne 0){throw 'Native build failed.'}}
$native=Join-Path $PSScriptRoot 'Native\bin\Release\OfflineTeamBuilderNative.dll'
if(-not(Test-Path -LiteralPath $native)) {
    $distributed=Join-Path $PSScriptRoot '..\dist\v2.1.7\OfflineTeamBuilderNative.dll'
    if(-not(Test-Path -LiteralPath $distributed)){throw 'Native build input missing. Use -RebuildNative with MSVC, or supply the checked source-package helper.'}
    New-Item -ItemType Directory -Path (Split-Path $native -Parent) -Force|Out-Null
    Copy-Item -LiteralPath $distributed -Destination $native
}
$manifestPath=Join-Path $PSScriptRoot 'Native\native-helper-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw|ConvertFrom-Json
if((Get-FileHash -LiteralPath $native).Hash -ine $manifest.sha256){throw 'Native helper does not match its embedded manifest.'}
New-Item -ItemType Directory -Path $out|Out-Null
& dotnet build (Join-Path $PSScriptRoot 'tests\RuntimeChecks.csproj') -c Release --nologo "-p:MmcRoot=$manager" 2>&1|Tee-Object -FilePath (Join-Path $out 'build.log')
if($LASTEXITCODE -ne 0){throw 'Managed build failed.'}
$checkOutput=& (Join-Path $PSScriptRoot 'tests\bin\Release\net48\RuntimeChecks.exe') 2>&1
$checkExit=$LASTEXITCODE
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
@{version=$version;managedChecks=$checks;supportCollectorChecks=$true;managedSha256=(Get-FileHash -LiteralPath $dll).Hash.ToLowerInvariant();nativeSha256=$manifest.sha256;runtimeDirectory=$release;inGameAcceptance='Tester confirmed 2.1.6 startup fix. 2.1.7 data paths/logging have private ACL/migration checks; no new live game test claimed.'}|
    ConvertTo-Json|Set-Content -LiteralPath (Join-Path $out 'build-validation.json') -Encoding UTF8
Write-Output ('Checked runtime staged: '+$release)
