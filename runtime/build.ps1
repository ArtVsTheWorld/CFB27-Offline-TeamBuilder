[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MmcRoot,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts')
)
$ErrorActionPreference = 'Stop'
$manager = (Resolve-Path -LiteralPath $MmcRoot).Path
& dotnet build (Join-Path $PSScriptRoot 'tests\RuntimeChecks.csproj') -c Release --nologo "-p:MmcRoot=$manager"
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& (Join-Path $PSScriptRoot 'tests\bin\Release\net48\RuntimeChecks.exe')
if ($LASTEXITCODE -ne 0) { throw 'Runtime checks failed. No release packaged.' }
$dll = Join-Path $PSScriptRoot 'bin\Release\net48\OfflineTeamBuilderRuntime.dll'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Expected semantic release version.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$release = Join-Path $output "OfflineTeamBuilderRuntime-$version"
if (Test-Path -LiteralPath $release) { throw "Release folder exists; select a fresh OutputDirectory: $release" }
New-Item -ItemType Directory -Path $release | Out-Null
Copy-Item -LiteralPath $dll -Destination $release
foreach ($name in @('README.md','CHANGELOG.md','TROUBLESHOOTING.md','NOTICE.md','install.ps1','collect-diagnostics.ps1','OfflineTeamBuilderRuntime.settings.example.json')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $release
}
$sha = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
@{name='Offline TeamBuilder Runtime';version=$version;authors='JDHalfRack / Ace / Timberline Tex';profile='cfb27-2026-10-19';files=@(@{name='OfflineTeamBuilderRuntime.dll';sha256=$sha})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $release 'release.json') -Encoding UTF8
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'tests\PackagingChecks.ps1') -ReleaseDirectory $release
if ($LASTEXITCODE -ne 0) { throw 'Packaging safety checks failed. No release ZIP created.' }
Compress-Archive -Path (Join-Path $release '*') -DestinationPath "$release.zip"
$source = Join-Path $output "OfflineTeamBuilderRuntime-$version-source"
New-Item -ItemType Directory -Path $source | Out-Null
foreach ($name in @('src','Profiles')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source -Recurse }
New-Item -ItemType Directory -Path (Join-Path $source 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\rebrand_fbmod.py') -Destination (Join-Path $source 'tools')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\capture-readiness.ps1') -Destination (Join-Path $source 'tools')
foreach($name in @('export-fbproject.ps1','verify_fbproject.py')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "tools\$name") -Destination (Join-Path $source 'tools') }
New-Item -ItemType Directory -Path (Join-Path $source 'tests') | Out-Null
foreach ($file in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -File) { Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $source 'tests') }
foreach ($name in @('OfflineTeamBuilderRuntime.csproj','build.ps1','package-release.ps1','install.ps1','collect-diagnostics.ps1','OfflineTeamBuilderRuntime.settings.example.json','USER-README.txt','README.md','BUILD.md','UPDATING.md','TROUBLESHOOTING.md','CHANGELOG.md','NOTICE.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $source
}
Compress-Archive -Path (Join-Path $source '*') -DestinationPath "$source.zip"
Write-Host "Release: $release.zip"
Write-Host "Source: $source.zip"
Write-Host "Plugin SHA256 (package integrity only, not game EXE identity): $sha"
