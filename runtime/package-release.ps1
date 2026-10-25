[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MmcRoot,
    [Parameter(Mandatory=$true)][string]$WorkingFbmod,
    [Parameter(Mandatory=$true)][string]$ExpectedFbmodHash,
    [Parameter(Mandatory=$true)][string]$ExpectedDllHash,
    [Parameter(Mandatory=$true)][string]$CacheIndex,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Choose a new OutputDirectory; release checkpoints are not overwritten.' }
$fbmod = (Resolve-Path -LiteralPath $WorkingFbmod).Path
if ((Get-FileHash -LiteralPath $fbmod -Algorithm SHA256).Hash -ne $ExpectedFbmodHash) { throw 'Working fbmod selection changed. This checks the package, never the game EXE.' }
& (Join-Path $PSScriptRoot 'build.ps1') -MmcRoot $MmcRoot -OutputDirectory (Join-Path $output 'Developer')
$dll = Join-Path $PSScriptRoot 'bin\Release\net48\OfflineTeamBuilderRuntime.dll'
if ((Get-FileHash -LiteralPath $dll).Hash -ne $ExpectedDllHash) { throw 'Built DLL differs from the selected game-tested DLL. No user release packaged.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
$release = Join-Path $output "OfflineTeamBuilder-$version"
New-Item -ItemType Directory -Path (Join-Path $release 'Plugins') -Force | Out-Null
Copy-Item -LiteralPath $dll -Destination (Join-Path $release 'Plugins\OfflineTeamBuilderRuntime.dll')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'USER-README.txt') -Destination (Join-Path $release 'README.txt')
$modOutput = Join-Path $release 'OfflineTeamBuilder.fbmod'
$metadata = & python (Join-Path $PSScriptRoot 'tools\rebrand_fbmod.py') $fbmod $modOutput --version $version --expected-source $ExpectedFbmodHash
if ($LASTEXITCODE -ne 0) { throw 'Fbmod metadata update failed; no user release archive created.' }
$report = ($metadata -join "`n") | ConvertFrom-Json
if (!$report.game_compatibility_unchanged -or !$report.resource_records_unchanged -or !$report.manifest_and_payloads_unchanged) { throw 'Fbmod preservation check failed.' }
if ($report.details[1] -cne 'JDHalfRack / Ace / Timberline Tex' -or $report.details[3] -cne $version) { throw 'Fbmod version/author mismatch.' }
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\VerifyFbmod.ps1') -MmcRoot $MmcRoot -Baseline $fbmod -Candidate $modOutput -Version $version
if ($LASTEXITCODE -ne 0) { throw 'MMC fbmod reader check failed; no user release archive created.' }
$projectOutput=Join-Path $release 'OfflineTeamBuilder.fbproject'
$projectReport=Join-Path $output 'project-verification.json'
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tools\export-fbproject.ps1') -MmcRoot $MmcRoot -Fbmod $modOutput -CacheIndex $CacheIndex -Destination $projectOutput -ReportPath $projectReport
if ($LASTEXITCODE -ne 0) { throw 'Editable project export failed; no user release archive created.' }
$projectCheck=& python (Join-Path $PSScriptRoot 'tools\verify_fbproject.py') $projectOutput $projectReport
if ($LASTEXITCODE -ne 0) { throw 'Editable project verification failed; no user release archive created.' }
Write-Host ($projectCheck -join "`n")
$files = @(Get-ChildItem -LiteralPath $release -Recurse -File)
if ($files.Count -ne 4) { throw 'User release must contain exactly the DLL, fbmod, fbproject and README.' }
$zip = "$release.zip"
Compress-Archive -Path (Join-Path $release '*') -DestinationPath $zip
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $entries = @($archive.Entries | Where-Object { $_.Name })
    if ($entries.Count -ne 4) { throw 'Release ZIP file count mismatch.' }
    foreach ($entry in $entries) {
        $relative = $entry.FullName.Replace('/','\')
        if ($relative -cnotin @('OfflineTeamBuilder.fbmod','OfflineTeamBuilder.fbproject','Plugins\OfflineTeamBuilderRuntime.dll','README.txt')) { throw "Unexpected ZIP entry: $relative" }
        $stream=$entry.Open();$sha=[Security.Cryptography.SHA256]::Create()
        try { $actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose();$stream.Dispose() }
        if ($actual -ne (Get-FileHash -LiteralPath (Join-Path $release $relative)).Hash) { throw "ZIP bytes differ: $relative" }
    }
} finally { $archive.Dispose() }
$report | Add-Member -NotePropertyName plugin_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $dll).Hash
$report | Add-Member -NotePropertyName project_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $projectOutput).Hash
$report | Add-Member -NotePropertyName release_sha256 -NotePropertyValue (Get-FileHash -LiteralPath $zip).Hash
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'package-verification.json') -Encoding UTF8
Copy-Item -LiteralPath (Join-Path $output "Developer\OfflineTeamBuilderRuntime-$version-source.zip") -Destination (Join-Path $output "OfflineTeamBuilder-$version-source.zip")
Write-Host "User release: $zip"
Write-Host "Updated source: $(Join-Path $output "OfflineTeamBuilder-$version-source.zip")"
Write-Host 'No installed files changed. The existing working fbmod was read only.'
