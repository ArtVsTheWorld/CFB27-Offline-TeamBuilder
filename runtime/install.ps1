[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MmcRoot,
    [string]$RestoreFrom,
    [string]$BackupRoot,
    [switch]$ValidateOnly
)
$ErrorActionPreference = 'Stop'
$names = @('OfflineTeamBuilder.dll','OfflineTeamBuilderReentry.dll','OfflineTeamBuilderRuntime.dll')
$manager = (Resolve-Path -LiteralPath $MmcRoot).Path
$plugins = Join-Path $manager 'Plugins'
if (!(Test-Path -LiteralPath (Join-Path $manager 'FrostyCore.dll')) -or !(Test-Path -LiteralPath $plugins -PathType Container)) { throw 'MmcRoot must be the MMC folder containing FrostyCore.dll and Plugins.' }
if (!$BackupRoot) { $BackupRoot = Join-Path $plugins 'OfflineTeamBuilderRuntime\Backups' }
function Get-Digest([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Get-SourcePath($File) {
    $storage = if ($File.storage) { [string]$File.storage } else { [string]$File.name }
    if ($storage -cne [string]$File.name -and $storage -cne ([string]$File.name+'.backup')) { throw 'Invalid backup storage filename.' }
    return Join-Path $source $storage
}
function Assert-Closed {
    $running = @(Get-Process -Name CollegeFB27,MMCModManager -ErrorAction SilentlyContinue)
    if ($running.Count) { throw 'Close College Football 27 and MMC before installing or rolling back. No files changed.' }
}
if ($RestoreFrom) {
    $source = (Resolve-Path -LiteralPath $RestoreFrom).Path
    $record = Get-Content -LiteralPath (Join-Path $source 'backup.json') -Raw | ConvertFrom-Json
    if (![string]::Equals([IO.Path]::GetFullPath($record.pluginDirectory),[IO.Path]::GetFullPath($plugins),[StringComparison]::OrdinalIgnoreCase)) { throw 'This backup belongs to a different MMC Plugins folder.' }
    $desired = @($record.files)
} else {
    $source = $PSScriptRoot
    $record = Get-Content -LiteralPath (Join-Path $source 'release.json') -Raw | ConvertFrom-Json
    $desired = @($record.files)
    if ($desired.Count -ne 1 -or $desired[0].name -ne 'OfflineTeamBuilderRuntime.dll') { throw 'Invalid single-DLL release manifest.' }
}
if (@($desired | Group-Object name | Where-Object Count -gt 1).Count) { throw 'Duplicate filename in manifest.' }
foreach ($file in $desired) {
    if ($names -cnotcontains [string]$file.name) { throw 'Manifest contains an out-of-scope filename.' }
    if ((Get-Digest (Get-SourcePath $file)) -ne $file.sha256) { throw "Plugin package integrity check failed: $($file.name). This is NOT a game executable hash check." }
}
Write-Host "Destination: $plugins"
Write-Host ('Install/restore: ' + (($desired | ForEach-Object name) -join ', '))
if ($ValidateOnly) { Write-Host 'Validation passed; no files changed.'; return }
Assert-Closed
$backupParent = [IO.Path]::GetFullPath($BackupRoot)
# New backups use .backup, never .dll, so MMC cannot load archived plugins.
if ([string]::Equals($backupParent.TrimEnd('\'),[IO.Path]::GetFullPath($plugins).TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)) { throw 'Use a backup subfolder, not the Plugins root.' }
$backup = Join-Path $backupParent ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$saved = @()
foreach ($name in $names) {
    $original = Join-Path $plugins $name
    if (Test-Path -LiteralPath $original) {
        $hash = Get-Digest $original
        $storage = $name+'.backup'
        Copy-Item -LiteralPath $original -Destination (Join-Path $backup $storage)
        if ((Get-Digest (Join-Path $backup $storage)) -ne $hash) { throw 'Backup verification failed; installed files have not changed.' }
        $saved += [pscustomobject]@{name=$name;storage=$storage;sha256=$hash}
    }
}
@{pluginDirectory=$plugins;createdUtc=[DateTime]::UtcNow.ToString('O');files=@($saved)} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'backup.json') -Encoding UTF8
Assert-Closed
try {
    foreach ($file in $desired) {
        Copy-Item -LiteralPath (Get-SourcePath $file) -Destination (Join-Path $plugins $file.name) -Force
        if ((Get-Digest (Join-Path $plugins $file.name)) -ne $file.sha256) { throw "Installed plugin verification failed: $($file.name)" }
    }
    foreach ($name in $names) {
        if (@($desired | Where-Object name -eq $name).Count -eq 0 -and (Test-Path -LiteralPath (Join-Path $plugins $name))) {
            Remove-Item -LiteralPath (Join-Path $plugins $name)
        }
    }
} catch {
    $failure = $_
    foreach ($name in $names) {
        $path = Join-Path $plugins $name
        if (Test-Path -LiteralPath (Join-Path $backup ($name+'.backup'))) { Copy-Item -LiteralPath (Join-Path $backup ($name+'.backup')) -Destination $path -Force }
        elseif (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path }
    }
    throw "Install failed; prior DLL set restored from $backup. $failure"
}
Write-Host "Complete. Previous DLL set preserved at: $backup"
Write-Host "Rollback: .\install.ps1 -MmcRoot `"$manager`" -RestoreFrom `"$backup`""
Write-Host 'Only the three named plugin DLLs were changed. No fbmod, saves, settings, asset cache, or unrelated plugins were changed.'
