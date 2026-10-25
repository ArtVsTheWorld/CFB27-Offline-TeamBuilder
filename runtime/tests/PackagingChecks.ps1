[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('tbr-package-test-'+[Guid]::NewGuid().ToString('N'))
$manager = Join-Path $fixture 'MockMMC'
$plugins = Join-Path $manager 'Plugins'
$backups = Join-Path $fixture 'Backups'
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
[IO.File]::WriteAllBytes((Join-Path $manager 'FrostyCore.dll'),[byte[]]@(1,2,3))
[IO.File]::WriteAllBytes((Join-Path $plugins 'OfflineTeamBuilder.dll'),[byte[]]@(4,5,6))
[IO.File]::WriteAllBytes((Join-Path $plugins 'OfflineTeamBuilderReentry.dll'),[byte[]]@(7,8,9))
[IO.File]::WriteAllBytes((Join-Path $plugins 'UnrelatedPlugin.dll'),[byte[]]@(10,11,12))
$script:checks = 0
function Assert($ok,[string]$message) { if (!$ok) { throw "FAILED: $message" }; $script:checks++ }
function Digest([string]$path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
$main = Digest (Join-Path $plugins 'OfflineTeamBuilder.dll')
$assets = Digest (Join-Path $plugins 'OfflineTeamBuilderReentry.dll')
$other = Digest (Join-Path $plugins 'UnrelatedPlugin.dll')
# These tests target a private dummy MMC folder, never the real manager. Simulate
# process presence/absence in this PowerShell process only; install.ps1 has no bypass.
$global:tbrFixtureSimulateRunning = $true
function Get-Process { param($Name,$ErrorAction); if ($global:tbrFixtureSimulateRunning) { [pscustomobject]@{ProcessName='CollegeFB27'} } }
$install = Join-Path $release 'install.ps1'
& $install -MmcRoot $manager -BackupRoot $backups -ValidateOnly
Assert (!(Test-Path -LiteralPath $backups)) 'validation is read-only'
$rejected = $false
try { & $install -MmcRoot $manager -BackupRoot $backups } catch { $rejected=$true }
Assert $rejected 'running game blocks writes'
Assert (!(Test-Path -LiteralPath $backups)) 'blocked install did not create backup or mutate plugins'
$global:tbrFixtureSimulateRunning = $false
& $install -MmcRoot $manager -BackupRoot $backups
Assert (!(Test-Path -LiteralPath (Join-Path $plugins 'OfflineTeamBuilder.dll'))) 'legacy main removed from Plugins'
Assert (!(Test-Path -LiteralPath (Join-Path $plugins 'OfflineTeamBuilderReentry.dll'))) 'legacy companion removed from Plugins'
Assert ((Digest (Join-Path $plugins 'OfflineTeamBuilderRuntime.dll')) -eq (Digest (Join-Path $release 'OfflineTeamBuilderRuntime.dll'))) 'single runtime installed byte-exact'
Assert ((Digest (Join-Path $plugins 'UnrelatedPlugin.dll')) -eq $other) 'unrelated plugin preserved'
$originalBackup = @(Get-ChildItem -LiteralPath $backups -Directory)[0].FullName
Assert ((Digest (Join-Path $originalBackup 'OfflineTeamBuilder.dll.backup')) -eq $main) 'original main backed up'
Assert ((Digest (Join-Path $originalBackup 'OfflineTeamBuilderReentry.dll.backup')) -eq $assets) 'original companion backed up'
Assert (@(Get-ChildItem -LiteralPath $backups -Recurse -Filter '*.dll').Count -eq 0) 'backups cannot be discovered as loadable DLLs'
& $install -MmcRoot $manager -BackupRoot $backups -RestoreFrom $originalBackup
Assert ((Digest (Join-Path $plugins 'OfflineTeamBuilder.dll')) -eq $main -and (Digest (Join-Path $plugins 'OfflineTeamBuilderReentry.dll')) -eq $assets) 'rollback restores exact pair'
Assert (!(Test-Path -LiteralPath (Join-Path $plugins 'OfflineTeamBuilderRuntime.dll'))) 'rollback removes unified DLL'
Assert (@(Get-ChildItem -LiteralPath $backups -Directory).Count -eq 2) 'rollback itself is recoverable'
$rejected = $false
try { & $install -MmcRoot $manager -BackupRoot $plugins } catch { $rejected=$true }
Assert $rejected 'backups cannot spill into Plugins root'
$bad = Join-Path $fixture 'CorruptPackage'
New-Item -ItemType Directory -Path $bad | Out-Null
foreach ($name in @('install.ps1','release.json')) { Copy-Item -LiteralPath (Join-Path $release $name) -Destination $bad }
[IO.File]::WriteAllBytes((Join-Path $bad 'OfflineTeamBuilderRuntime.dll'),[byte[]]@(13,14))
$rejected = $false
try { & (Join-Path $bad 'install.ps1') -MmcRoot $manager -BackupRoot $backups } catch { $rejected=$true }
Assert $rejected 'corrupt package rejected before writes'
Assert ((Digest (Join-Path $plugins 'OfflineTeamBuilder.dll')) -eq $main) 'rejected package leaves pair intact'
$logs = Join-Path $fixture 'MockLogs'
$support = Join-Path $fixture 'Support'
New-Item -ItemType Directory -Path $logs | Out-Null
[IO.File]::WriteAllText((Join-Path $logs 'runtime.log'),'mock runtime log')
[IO.File]::WriteAllText((Join-Path $logs 'status.json'),'{}')
[IO.File]::WriteAllText((Join-Path $logs 'private-config.json'),'not for support')
& (Join-Path $release 'collect-diagnostics.ps1') -MmcRoot $manager -DiagnosticsRoot $logs -OutputDirectory $support
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead(@(Get-ChildItem -LiteralPath $support -Filter '*.zip')[0].FullName)
try {
    $entries = @($archive.Entries | ForEach-Object FullName)
    Assert ($entries.Count -eq 3 -and $entries -contains 'runtime.log' -and $entries -contains 'status.json' -and $entries -contains 'support.json') 'support ZIP uses explicit allow-list'
    Assert ($entries -notcontains 'private-config.json') 'private files excluded'
} finally { $archive.Dispose() }
# Exercise new defaults, rather than only explicit mock output overrides.
& $install -MmcRoot $manager
$dataRoot = Join-Path $plugins 'OfflineTeamBuilderRuntime'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Backups') -Filter 'backup.json' -Recurse).Count -eq 1) 'default backup stays under plugin data folder'
Assert (@(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Backups') -Filter '*.dll' -Recurse).Count -eq 0) 'default backup is not a duplicate plugin'
[IO.File]::WriteAllText((Join-Path $dataRoot 'runtime.log'),'default-path mock log')
& (Join-Path $release 'collect-diagnostics.ps1') -MmcRoot $manager
Assert (@(Get-ChildItem -LiteralPath (Join-Path $dataRoot 'Support') -Filter '*.zip').Count -eq 1) 'default support archive stays under plugin data folder'
Write-Host "PASS $script:checks packaging checks. Private mock installation only. Review fixture: $fixture"
